using Microsoft.Win32;

namespace MdReader.Shell;

/// <summary>
/// Registers mdreader as an *available* handler for markdown files, per-user,
/// exactly as specified in §4.1 — and never claims the association outright.
///
/// What this class will never do (§4.2):
///  - write to Explorer\FileExts\...\UserChoice (protected by a per-user hash
///    since Windows 8; writing it fails, gets reverted, or gets the app flagged
///    as a hijacker),
///  - overwrite HKCU\Software\Classes\.md\(Default) (that would clobber the
///    user's existing handler — OpenWithProgids is additive).
/// Becoming the default is always the user's explicit action through the
/// standard Windows UI (see <see cref="DefaultAppPrompt"/>).
/// </summary>
public sealed class FileAssociationRegistrar
{
    public const string ProgId = "MdReader.Markdown.1";
    public const string AppRegistrationName = "mdreader";
    internal const string OpenVerbName = "mdreader.open";
    internal const string ExportPdfVerbName = "mdreader.exportPdf";

    private readonly RegistryKey _root;

    /// <summary>
    /// Production: pass Registry.CurrentUser. Tests: pass a sandbox subkey that
    /// stands in for HKCU so assertions can enumerate exactly what was written.
    /// </summary>
    public FileAssociationRegistrar(RegistryKey? root = null)
    {
        _root = root ?? Registry.CurrentUser;
    }

    /// <summary>
    /// The DefaultIcon value: the document icon shipped beside the exe, or the
    /// exe's own icon (index 0) when the file is missing.
    /// </summary>
    internal static string DocumentIconValue(string exePath)
    {
        var iconPath = Path.Combine(Path.GetDirectoryName(exePath) ?? string.Empty, "mdreader-doc.ico");
        return File.Exists(iconPath) ? $"\"{iconPath}\"" : $"\"{exePath}\",0";
    }

    /// <summary>Registers the ProgId, capability declarations, and OpenWithProgids entries.</summary>
    public void Register(string exePath, IReadOnlyCollection<string> extensions)
    {
        var documentIcon = DocumentIconValue(exePath);

        // ProgId with shell verbs: open (reader) and edit (source mode).
        using (var progId = _root.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progId.SetValue(null, "Markdown Document");
            progId.SetValue("FriendlyTypeName", "Markdown Document");

            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue(null, documentIcon);

            using var open = progId.CreateSubKey(@"shell\open\command");
            open.SetValue(null, $"\"{exePath}\" \"%1\"");

            using var edit = progId.CreateSubKey(@"shell\edit\command");
            edit.SetValue(null, $"\"{exePath}\" --source \"%1\"");
        }

        // Applications entry so mdreader appears in "Open with". It gets its own
        // DefaultIcon because Windows records an Open With choice as
        // "Applications\mdreader.exe" — without an icon here, Explorer shows a
        // blank document icon for associated files.
        using (var application = _root.CreateSubKey(@"Software\Classes\Applications\mdreader.exe"))
        {
            application.SetValue("FriendlyAppName", "mdreader");

            using var appIcon = application.CreateSubKey("DefaultIcon");
            appIcon.SetValue(null, documentIcon);

            using var shellOpen = application.CreateSubKey(@"shell\open\command");
            shellOpen.SetValue(null, $"\"{exePath}\" \"%1\"");

            using var supported = application.CreateSubKey("SupportedTypes");
            foreach (var extension in extensions)
            {
                supported.SetValue(extension, string.Empty);
            }
        }

        // Capabilities + RegisteredApplications: what makes mdreader show up in
        // Settings → Default apps.
        using (var capabilities = _root.CreateSubKey(@"Software\mdreader\Capabilities"))
        {
            capabilities.SetValue("ApplicationName", "mdreader");
            capabilities.SetValue("ApplicationDescription", "Fast markdown reader and editor");

            using var associations = capabilities.CreateSubKey("FileAssociations");
            foreach (var extension in extensions)
            {
                associations.SetValue(extension, ProgId);
            }
        }

        using (var registered = _root.CreateSubKey(@"Software\RegisteredApplications"))
        {
            registered.SetValue(AppRegistrationName, @"Software\mdreader\Capabilities");
        }

        // ADDITIVE per-extension registration. Never the (Default) value.
        foreach (var extension in extensions)
        {
            using var openWith = _root.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids");
            openWith.SetValue(ProgId, string.Empty, RegistryValueKind.String);
            RegisterExplorerVerbs(exePath, extension);
        }

        ShellNotify.AssociationsChanged();
    }

    /// <summary>
    /// Removes exactly the keys and values <see cref="Register"/> wrote.
    /// UserChoice is never touched; Windows decides what .md falls back to.
    /// </summary>
    public void Unregister(IReadOnlyCollection<string> extensions)
    {
        _root.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
        _root.DeleteSubKeyTree(@"Software\Classes\Applications\mdreader.exe", throwOnMissingSubKey: false);
        _root.DeleteSubKeyTree(@"Software\mdreader\Capabilities", throwOnMissingSubKey: false);

        using (var mdreaderKey = _root.OpenSubKey(@"Software\mdreader", writable: true))
        {
            // Remove the parent only when Capabilities was its sole content.
            if (mdreaderKey is not null && mdreaderKey.SubKeyCount == 0 && mdreaderKey.ValueCount == 0)
            {
                _root.DeleteSubKey(@"Software\mdreader", throwOnMissingSubKey: false);
            }
        }

        using (var registered = _root.OpenSubKey(@"Software\RegisteredApplications", writable: true))
        {
            registered?.DeleteValue(AppRegistrationName, throwOnMissingValue: false);
        }

        foreach (var extension in extensions)
        {
            using var openWith = _root.OpenSubKey($@"Software\Classes\{extension}\OpenWithProgids", writable: true);
            if (openWith is null)
            {
                continue;
            }

            openWith.DeleteValue(ProgId, throwOnMissingValue: false);

            // If OpenWithProgids is now empty and the extension key has nothing
            // else mdreader-related, leave the rest of the key alone — other
            // apps' data under it is not ours to clean up.

            var shellBase = $@"Software\Classes\SystemFileAssociations\{extension}\shell";
            _root.DeleteSubKeyTree($@"{shellBase}\{OpenVerbName}", throwOnMissingSubKey: false);
            _root.DeleteSubKeyTree($@"{shellBase}\{ExportPdfVerbName}", throwOnMissingSubKey: false);
        }

        ShellNotify.AssociationsChanged();
    }

    private void RegisterExplorerVerbs(string exePath, string extension)
    {
        var shellBase = $@"Software\Classes\SystemFileAssociations\{extension}\shell";

        using (var open = _root.CreateSubKey($@"{shellBase}\{OpenVerbName}"))
        {
            open.SetValue(null, "Open in mdreader");
            open.SetValue("Icon", $"\"{exePath}\",0");
            using var command = open.CreateSubKey("command");
            command.SetValue(null, $"\"{exePath}\" \"%1\"");
        }

        using (var export = _root.CreateSubKey($@"{shellBase}\{ExportPdfVerbName}"))
        {
            export.SetValue(null, "Export to PDF…");
            export.SetValue("Icon", $"\"{exePath}\",0");
            using var command = export.CreateSubKey("command");
            command.SetValue(null, $"\"{exePath}\" \"%1\" --export-pdf-prompt");
        }
    }

    /// <summary>True when the ProgId's open command points at this exe.</summary>
    public bool IsRegistered(string exePath)
    {
        using var open = _root.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        return open?.GetValue(null) is string command &&
               command.Contains(exePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The ProgId the user has chosen for an extension (from UserChoice, read
    /// only — reading is fine, writing is not), or null when unset.
    /// </summary>
    public string? GetUserChoiceProgId(string extension)
    {
        using var key = _root.OpenSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice");
        return key?.GetValue("ProgId") as string;
    }

    /// <summary>
    /// True when mdreader is the user-chosen default for the extension.
    /// Windows records the choice as our ProgId when the user picks the file
    /// type entry, but as "Applications\mdreader.exe" when they pick the app
    /// from the Open With list — both mean "mdreader is the default".
    /// </summary>
    public bool IsDefaultFor(string extension)
    {
        var userChoice = GetUserChoiceProgId(extension);
        return string.Equals(userChoice, ProgId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(userChoice, @"Applications\mdreader.exe", StringComparison.OrdinalIgnoreCase);
    }
}
