using System.Diagnostics;
using System.Text;
using FluentAssertions;

namespace MdReader.Integration.Tests;

[Trait("suite", "blocking")]
public sealed class DeterministicSaveTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"mdreader-save-{Guid.NewGuid():N}.md");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.SetAttributes(_path, FileAttributes.Normal);
            File.Delete(_path);
        }
    }

    [Fact]
    public void Production_save_path_preserves_utf8_bom_and_crlf()
    {
        File.WriteAllBytes(_path, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("# Old\r\n\r\nText\r\n")]);
        // Monaco is configured to the opened file's EOL before returning the
        // edited buffer, so the production save receives CRLF here.
        var replacement = Convert.ToBase64String(Encoding.UTF8.GetBytes("# New\r\n\r\nSaved\r\n"));

        Run($"\"{_path}\" --test-save-text {replacement}").Should().Be(0);

        var bytes = File.ReadAllBytes(_path);
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        Encoding.UTF8.GetString(bytes[3..]).Should().Be("# New\r\n\r\nSaved\r\n");
    }

    [Fact]
    public void Failed_save_keeps_original_bytes()
    {
        var original = Encoding.UTF8.GetBytes("original");
        File.WriteAllBytes(_path, original);
        File.SetAttributes(_path, FileAttributes.ReadOnly);
        var replacement = Convert.ToBase64String(Encoding.UTF8.GetBytes("replacement"));

        Run($"\"{_path}\" --test-save-text {replacement}").Should().Be(1);

        File.ReadAllBytes(_path).Should().Equal(original);
    }

    private static int Run(string arguments)
    {
        var start = new ProcessStartInfo(AppHarness.ExePath, arguments) { UseShellExecute = false };
        start.Environment["MDREADER_TEST_MODE"] = "1";
        using var process = Process.Start(start)!;
        process.WaitForExit(15_000).Should().BeTrue();
        return process.ExitCode;
    }
}
