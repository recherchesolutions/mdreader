using System.Diagnostics;
using System.Text;
using FluentAssertions;

namespace MdReader.Core.Tests;

public class RendererUnitTests
{
    private static readonly MarkdownRenderer Renderer = new();

    [Fact]
    public void Source_line_anchors_are_emitted()
    {
        var result = Renderer.Render("# One\n\nParagraph two.\n\n## Three\n");

        result.BodyHtml.Should().Contain("data-source-line=\"1\"");
        result.BodyHtml.Should().Contain("data-source-line=\"3\"");
        result.BodyHtml.Should().Contain("data-source-line=\"5\"");
    }

    [Fact]
    public void Headings_are_collected_with_github_style_ids()
    {
        var result = Renderer.Render("# Hello World\n\n## Second Heading!\n\ntext\n\n### Third\n");

        result.Headings.Should().HaveCount(3);
        result.Headings[0].Should().BeEquivalentTo(new HeadingInfo(1, "Hello World", "hello-world", 1));
        result.Headings[1].Id.Should().Be("second-heading");
        result.Title.Should().Be("Hello World");
    }

    [Fact]
    public void Front_matter_renders_as_metadata_card_not_text()
    {
        var result = Renderer.Render("---\ntitle: My Doc\nauthor: Someone\n---\n\n# Body\n");

        result.BodyHtml.Should().Contain("front-matter");
        result.BodyHtml.Should().Contain("My Doc");
        result.BodyHtml.Should().NotContain("<p>title:");
    }

    [Fact]
    public void Front_matter_values_are_html_encoded()
    {
        var result = Renderer.Render("---\ntitle: <script>alert(1)</script>\n---\n\nbody\n");

        result.BodyHtml.Should().NotContain("<script>");
        result.BodyHtml.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void Relative_image_is_rewritten_to_document_host()
    {
        var result = Renderer.Render("![x](images/a.png)", new RenderOptions
        {
            DocumentPath = @"C:\docs\project\notes\doc.md",
        });

        result.BodyHtml.Should().Contain($"src=\"{VirtualHosts.DocumentOrigin}/");
        result.DocumentRootPath.Should().Be(@"C:\"); // 3 parent levels above notes\
    }

    [Fact]
    public void Image_escaping_beyond_parent_limit_is_refused()
    {
        var result = Renderer.Render("![x](../../../../secret.png)", new RenderOptions
        {
            DocumentPath = @"C:\a\b\c\d\e\doc.md",
            MaxImagePathParentLevels = 3,
        });

        result.BodyHtml.Should().Contain("path-refused");
        result.BodyHtml.Should().NotContain("secret.png\" src");
    }

    [Fact]
    public void Image_within_parent_limit_is_allowed()
    {
        var result = Renderer.Render("![x](../../shared/logo.png)", new RenderOptions
        {
            DocumentPath = @"C:\a\b\c\d\e\doc.md",
            MaxImagePathParentLevels = 3,
        });

        result.BodyHtml.Should().Contain($"src=\"{VirtualHosts.DocumentOrigin}/");
        result.BodyHtml.Should().NotContain("path-refused");
    }

    [Fact]
    public void Remote_image_is_blocked_by_default_with_placeholder_data()
    {
        var result = Renderer.Render("![x](https://example.com/pixel.png)");

        result.BodyHtml.Should().Contain("remote-blocked");
        result.BodyHtml.Should().Contain("data-remote-src=\"https://example.com/pixel.png\"");
        result.BodyHtml.Should().NotContain(" src=\"https://example.com/pixel.png\"");
    }

    [Fact]
    public void Remote_image_loads_when_opted_in()
    {
        var result = Renderer.Render("![x](https://example.com/img.png)", new RenderOptions
        {
            AllowRemoteImages = true,
        });

        result.BodyHtml.Should().Contain("src=\"https://example.com/img.png\"");
        result.BodyHtml.Should().NotContain("remote-blocked");
    }

    [Fact]
    public void Wide_500_column_table_renders_without_error()
    {
        var sb = new StringBuilder();
        sb.Append('|').Append(string.Join('|', Enumerable.Range(1, 500).Select(i => $"c{i}"))).AppendLine("|");
        sb.Append('|').Append(string.Join('|', Enumerable.Repeat("---", 500))).AppendLine("|");
        sb.Append('|').Append(string.Join('|', Enumerable.Repeat("v", 500))).AppendLine("|");

        var result = Renderer.Render(sb.ToString());

        result.BodyHtml.Should().Contain("<table");
        result.BodyHtml.Should().Contain("c500");
    }

    [Fact]
    public void Five_megabyte_document_renders_in_reasonable_time()
    {
        var section = "## Section heading\n\nSome paragraph text with **bold** and `code`.\n\n- item one\n- item two\n\n";
        var sb = new StringBuilder("# Big document\n\n");
        while (sb.Length < 5 * 1024 * 1024)
        {
            sb.Append(section);
        }

        var sw = Stopwatch.StartNew();
        var result = Renderer.Render(sb.ToString());
        sw.Stop();

        result.BodyHtml.Should().NotBeEmpty();
        // Smoke guard against quadratic regressions only (Debug + test host is
        // ~8x slower); the benchmark project measures the real §6 targets in Release.
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void Unclosed_fence_does_not_crash()
    {
        var result = Renderer.Render("```csharp\nvar x = 1;\n\n# swallowed heading\n");
        result.BodyHtml.Should().Contain("<pre");
    }

    [Fact]
    public void Raw_html_is_escaped_by_default()
    {
        var result = Renderer.Render("<div class=\"x\">raw</div>\n");

        result.BodyHtml.Should().NotContain("<div class=\"x\">");
        result.BodyHtml.Should().Contain("&lt;div");
    }

    [Fact]
    public void Standalone_export_document_is_complete_html()
    {
        var result = Renderer.Render("# Export me\n\nBody.\n");
        var html = HtmlDocumentAssembler.BuildStandalone(result, "body { color: black; }");

        html.Should().StartWith("<!DOCTYPE html>");
        html.Should().Contain("<title>Export me</title>");
        html.Should().Contain("Content-Security-Policy");
        html.Should().Contain("Body.");
    }

    [Fact]
    public void Parent_traversal_counting_is_exact()
    {
        ImagePathRewriter.CountParentTraversals("a/b.png").Should().Be(0);
        ImagePathRewriter.CountParentTraversals("../a.png").Should().Be(1);
        ImagePathRewriter.CountParentTraversals("a/../../b.png").Should().Be(1);
        ImagePathRewriter.CountParentTraversals("../../../x.png").Should().Be(3);
        ImagePathRewriter.CountParentTraversals("a/b/../../../../x.png").Should().Be(2);
    }
}
