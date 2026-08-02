using System.Diagnostics;
using FluentAssertions;

namespace MdReader.Integration.Tests;

/// <summary>
/// Deterministic critical-path coverage: headless HTML and PDF export drive the
/// full open → render → enhance (mermaid/katex/hljs) → export pipeline with an
/// exit code, no UI interaction needed.
/// </summary>
[Trait("suite", "blocking")]
public sealed class HeadlessExportTests : IDisposable
{
    private readonly string _outDir;

    public HeadlessExportTests()
    {
        _outDir = Path.Combine(Path.GetTempPath(), $"mdreader-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_outDir);
    }

    public void Dispose() => Directory.Delete(_outDir, recursive: true);

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static int RunHeadless(string arguments)
    {
        var psi = new ProcessStartInfo(AppHarness.ExePath, arguments) { UseShellExecute = false };
        psi.Environment["MDREADER_INSTANCE_ID"] = Guid.NewGuid().ToString("N");
        using var process = Process.Start(psi)!;
        process.WaitForExit(120_000).Should().BeTrue("headless export must terminate");
        return process.ExitCode;
    }

    [Fact]
    public void Export_html_is_self_contained_and_rendered()
    {
        var output = Path.Combine(_outDir, "mermaid.html");
        RunHeadless($"\"{Fixture("mermaid.md")}\" --export-html \"{output}\"").Should().Be(0);

        var html = File.ReadAllText(output);
        html.Should().StartWith("<!DOCTYPE html>");
        html.Should().Contain("<svg", "mermaid diagrams are pre-rendered into the export");
        html.Should().Contain("mermaid-error", "the invalid diagram keeps its inline error note");
        html.Should().NotContain("<script", "exports carry no scripts");
    }

    [Fact]
    public void Export_pdf_produces_valid_pdf()
    {
        var output = Path.Combine(_outDir, "math.pdf");
        RunHeadless($"\"{Fixture("math.md")}\" --export-pdf \"{output}\"").Should().Be(0);

        var header = new byte[5];
        using (var stream = File.OpenRead(output))
        {
            stream.ReadExactly(header);
        }

        System.Text.Encoding.ASCII.GetString(header).Should().Be("%PDF-");
    }

    [Fact]
    public void Export_missing_file_fails_with_distinct_exit_code()
    {
        RunHeadless($"\"{Path.Combine(_outDir, "nope.md")}\" --export-html \"{Path.Combine(_outDir, "x.html")}\"")
            .Should().Be(2);
    }
}
