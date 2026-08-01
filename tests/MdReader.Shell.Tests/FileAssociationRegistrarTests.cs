using FluentAssertions;
using MdReader.Shell;
using Microsoft.Win32;

namespace MdReader.Shell.Tests;

/// <summary>
/// Registration writes exactly the expected keys and nothing else; uninstall
/// removes exactly those keys; and — the guardrail — UserChoice is never
/// touched. All tests run against a sandbox subkey standing in for HKCU.
/// </summary>
public sealed class FileAssociationRegistrarTests : IDisposable
{
    private const string ExePath = @"C:\Users\test\AppData\Local\Programs\mdreader\mdreader.exe";
    private static readonly string[] Extensions = [".md", ".markdown"];

    private readonly string _sandboxPath;
    private readonly RegistryKey _sandbox;

    public FileAssociationRegistrarTests()
    {
        _sandboxPath = $@"Software\mdreader-test-{Guid.NewGuid():N}";
        _sandbox = Registry.CurrentUser.CreateSubKey(_sandboxPath);
    }

    public void Dispose()
    {
        _sandbox.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_sandboxPath, throwOnMissingSubKey: false);
    }

    /// <summary>
    /// THE guardrail (write this test first, per spec §7): registration must
    /// never create anything under Explorer\FileExts — that is where UserChoice
    /// lives, it is hash-protected, and writing it is how markdown apps end up
    /// flagged as hijackers.
    /// </summary>
    [Fact]
    public void Register_never_touches_UserChoice_or_FileExts()
    {
        new FileAssociationRegistrar(_sandbox).Register(ExePath, Extensions);

        var allKeys = EnumerateAllKeys(_sandbox).ToList();

        allKeys.Should().NotContain(k => k.Contains("FileExts", StringComparison.OrdinalIgnoreCase));
        allKeys.Should().NotContain(k => k.Contains("UserChoice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Register_writes_exactly_the_expected_keys()
    {
        new FileAssociationRegistrar(_sandbox).Register(ExePath, Extensions);

        var allKeys = EnumerateAllKeys(_sandbox).ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] expected =
        [
            @"Software",
            @"Software\Classes",
            @"Software\Classes\MdReader.Markdown.1",
            @"Software\Classes\MdReader.Markdown.1\DefaultIcon",
            @"Software\Classes\MdReader.Markdown.1\shell",
            @"Software\Classes\MdReader.Markdown.1\shell\open",
            @"Software\Classes\MdReader.Markdown.1\shell\open\command",
            @"Software\Classes\MdReader.Markdown.1\shell\edit",
            @"Software\Classes\MdReader.Markdown.1\shell\edit\command",
            @"Software\Classes\Applications",
            @"Software\Classes\Applications\mdreader.exe",
            @"Software\Classes\Applications\mdreader.exe\shell",
            @"Software\Classes\Applications\mdreader.exe\shell\open",
            @"Software\Classes\Applications\mdreader.exe\shell\open\command",
            @"Software\Classes\Applications\mdreader.exe\SupportedTypes",
            @"Software\Classes\.md",
            @"Software\Classes\.md\OpenWithProgids",
            @"Software\Classes\.markdown",
            @"Software\Classes\.markdown\OpenWithProgids",
            @"Software\mdreader",
            @"Software\mdreader\Capabilities",
            @"Software\mdreader\Capabilities\FileAssociations",
            @"Software\RegisteredApplications",
        ];

        allKeys.Should().BeEquivalentTo(expected, "registration must write exactly these keys and nothing else");
    }

    [Fact]
    public void Register_declares_capability_and_additive_openwith()
    {
        new FileAssociationRegistrar(_sandbox).Register(ExePath, Extensions);

        using var progId = _sandbox.OpenSubKey(@"Software\Classes\MdReader.Markdown.1");
        progId!.GetValue(null).Should().Be("Markdown Document");
        progId.GetValue("FriendlyTypeName").Should().Be("Markdown Document");

        using var open = _sandbox.OpenSubKey(@"Software\Classes\MdReader.Markdown.1\shell\open\command");
        open!.GetValue(null).Should().Be($"\"{ExePath}\" \"%1\"");

        using var edit = _sandbox.OpenSubKey(@"Software\Classes\MdReader.Markdown.1\shell\edit\command");
        edit!.GetValue(null).Should().Be($"\"{ExePath}\" --source \"%1\"");

        using var openWith = _sandbox.OpenSubKey(@"Software\Classes\.md\OpenWithProgids");
        openWith!.GetValueNames().Should().Contain("MdReader.Markdown.1");

        // ADDITIVE: the extension's (Default) value must not be set by us.
        using var mdKey = _sandbox.OpenSubKey(@"Software\Classes\.md");
        mdKey!.GetValue(null).Should().BeNull("(Default) belongs to whatever handler the user already had");

        using var capabilities = _sandbox.OpenSubKey(@"Software\mdreader\Capabilities\FileAssociations");
        capabilities!.GetValue(".md").Should().Be("MdReader.Markdown.1");
        capabilities.GetValue(".markdown").Should().Be("MdReader.Markdown.1");

        using var registered = _sandbox.OpenSubKey(@"Software\RegisteredApplications");
        registered!.GetValue("mdreader").Should().Be(@"Software\mdreader\Capabilities");
    }

    [Fact]
    public void Register_does_not_clobber_existing_default_or_other_progids()
    {
        // Simulate another app already owning .md.
        using (var md = _sandbox.CreateSubKey(@"Software\Classes\.md"))
        {
            md.SetValue(null, "SomeOtherApp.md");
        }

        using (var openWith = _sandbox.CreateSubKey(@"Software\Classes\.md\OpenWithProgids"))
        {
            openWith.SetValue("SomeOtherApp.md", string.Empty);
        }

        new FileAssociationRegistrar(_sandbox).Register(ExePath, Extensions);

        using var mdKey = _sandbox.OpenSubKey(@"Software\Classes\.md");
        mdKey!.GetValue(null).Should().Be("SomeOtherApp.md", "the existing handler must be preserved");

        using var openWithAfter = _sandbox.OpenSubKey(@"Software\Classes\.md\OpenWithProgids");
        openWithAfter!.GetValueNames().Should().Contain(["SomeOtherApp.md", "MdReader.Markdown.1"]);
    }

    [Fact]
    public void Unregister_removes_exactly_what_was_written_and_leaves_others()
    {
        // Another app's data that must survive our uninstall.
        using (var md = _sandbox.CreateSubKey(@"Software\Classes\.md"))
        {
            md.SetValue(null, "SomeOtherApp.md");
        }

        using (var openWith = _sandbox.CreateSubKey(@"Software\Classes\.md\OpenWithProgids"))
        {
            openWith.SetValue("SomeOtherApp.md", string.Empty);
        }

        var registrar = new FileAssociationRegistrar(_sandbox);
        registrar.Register(ExePath, Extensions);
        registrar.Unregister(Extensions);

        _sandbox.OpenSubKey(@"Software\Classes\MdReader.Markdown.1").Should().BeNull();
        _sandbox.OpenSubKey(@"Software\Classes\Applications\mdreader.exe").Should().BeNull();
        _sandbox.OpenSubKey(@"Software\mdreader").Should().BeNull();

        using var registered = _sandbox.OpenSubKey(@"Software\RegisteredApplications");
        registered!.GetValue("mdreader").Should().BeNull();

        using var mdKey = _sandbox.OpenSubKey(@"Software\Classes\.md");
        mdKey!.GetValue(null).Should().Be("SomeOtherApp.md", "uninstall must not touch the user's handler");

        using var openWithAfter = _sandbox.OpenSubKey(@"Software\Classes\.md\OpenWithProgids");
        openWithAfter!.GetValueNames().Should().Contain("SomeOtherApp.md");
        openWithAfter.GetValueNames().Should().NotContain("MdReader.Markdown.1");
    }

    [Fact]
    public void Unregister_never_touches_FileExts()
    {
        // Pre-create a fake UserChoice to prove uninstall leaves it alone.
        using (var userChoice = _sandbox.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.md\UserChoice"))
        {
            userChoice.SetValue("ProgId", "SomeOtherApp.md");
        }

        var registrar = new FileAssociationRegistrar(_sandbox);
        registrar.Register(ExePath, Extensions);
        registrar.Unregister(Extensions);

        using var userChoiceAfter = _sandbox.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.md\UserChoice");
        userChoiceAfter.Should().NotBeNull();
        userChoiceAfter!.GetValue("ProgId").Should().Be("SomeOtherApp.md");
    }

    [Fact]
    public void IsRegistered_and_user_choice_queries_work()
    {
        var registrar = new FileAssociationRegistrar(_sandbox);
        registrar.IsRegistered(ExePath).Should().BeFalse();

        registrar.Register(ExePath, Extensions);
        registrar.IsRegistered(ExePath).Should().BeTrue();

        registrar.GetUserChoiceProgId(".md").Should().BeNull();
        registrar.IsDefaultFor(".md").Should().BeFalse();

        using (var userChoice = _sandbox.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.md\UserChoice"))
        {
            userChoice.SetValue("ProgId", "MdReader.Markdown.1");
        }

        registrar.IsDefaultFor(".md").Should().BeTrue();
    }

    private static IEnumerable<string> EnumerateAllKeys(RegistryKey root, string prefix = "")
    {
        foreach (var name in root.GetSubKeyNames())
        {
            var path = prefix.Length == 0 ? name : $@"{prefix}\{name}";
            yield return path;

            using var child = root.OpenSubKey(name);
            if (child is not null)
            {
                foreach (var nested in EnumerateAllKeys(child, path))
                {
                    yield return nested;
                }
            }
        }
    }
}
