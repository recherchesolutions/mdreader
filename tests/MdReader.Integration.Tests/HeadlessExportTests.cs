using System.Diagnostics;
using System.Globalization;
using AngleSharp.Html.Parser;
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
    public void Export_html_keeps_all_diagram_nodes_inside_the_svg_viewbox()
    {
        var output = Path.Combine(_outDir, "mermaid-sizing.html");
        RunHeadless($"\"{Fixture("mermaid-sizing.md")}\" --export-html \"{output}\"").Should().Be(0);

        var document = new HtmlParser().ParseDocument(File.ReadAllText(output));
        var diagrams = document.QuerySelectorAll(".mermaid-diagram > svg");
        diagrams.Should().HaveCount(2);
        document.QuerySelectorAll(".mermaid-error").Should().BeEmpty();
        diagrams[0].TextContent.Should().Contain("Deploy to DEV");
        diagrams[1].TextContent.Should().Contain("Production");

        // These fixture charts use rectangular nodes with a single translate.
        // Checking the SVG element's CSS size alone misses an incorrect viewBox:
        // the element can fit the page while its right/bottom nodes are clipped.
        foreach (var svg in diagrams)
        {
            var view = Numbers(svg.GetAttribute("viewBox")!);
            view.Should().HaveCount(4);
            var nodes = svg.QuerySelectorAll("g.node");
            nodes.Should().NotBeEmpty();
            foreach (var node in nodes)
            {
                var transform = node.GetAttribute("transform")!;
                transform.Should().StartWith("translate(").And.EndWith(")");
                var position = Numbers(transform["translate(".Length..^1]);
                position.Should().HaveCount(2);
                var rect = node.QuerySelector("rect.label-container")!;
                rect.Should().NotBeNull();
                var left = position[0] + double.Parse(rect.GetAttribute("x")!, CultureInfo.InvariantCulture);
                var top = position[1] + double.Parse(rect.GetAttribute("y")!, CultureInfo.InvariantCulture);
                var right = left + double.Parse(rect.GetAttribute("width")!, CultureInfo.InvariantCulture);
                var bottom = top + double.Parse(rect.GetAttribute("height")!, CultureInfo.InvariantCulture);
                left.Should().BeGreaterThanOrEqualTo(view[0] - 0.1);
                top.Should().BeGreaterThanOrEqualTo(view[1] - 0.1);
                right.Should().BeLessThanOrEqualTo(view[0] + view[2] + 0.1);
                bottom.Should().BeLessThanOrEqualTo(view[1] + view[3] + 0.1);
            }
        }
    }

    private static double[] Numbers(string value) => value
        .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
        .Select(part => double.Parse(part, CultureInfo.InvariantCulture))
        .ToArray();

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
