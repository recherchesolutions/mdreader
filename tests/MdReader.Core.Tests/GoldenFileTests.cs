namespace MdReader.Core.Tests;

/// <summary>
/// Golden-file tests: each fixture renders through the full pipeline (parse →
/// anchors → render → sanitize → image policy → metadata card) and the resulting
/// body HTML is snapshot-verified. Any rendering change shows up as a diff.
/// </summary>
public class GoldenFileTests
{
    private static readonly MarkdownRenderer Renderer = new();

    public static TheoryData<string> FixtureNames()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(TestSupport.FixturesDirectory, "*.md", SearchOption.AllDirectories))
        {
            data.Add(Path.GetRelativePath(TestSupport.FixturesDirectory, file).Replace('\\', '/'));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public Task Renders_fixture(string name)
    {
        var markdown = TestSupport.ReadFixture(name);
        var result = Renderer.Render(markdown, new RenderOptions
        {
            DocumentPath = TestSupport.FakeDocumentPath,
        });

        return Verifier.Verify(result.BodyHtml, extension: "html")
            .UseTextForParameters(name.Replace('/', '_').Replace(".md", ""));
    }

    [Fact]
    public Task Renders_raw_html_optin_still_sanitized()
    {
        var markdown = TestSupport.ReadFixture("xss/xss-raw-html.md");
        var result = Renderer.Render(markdown, new RenderOptions
        {
            DocumentPath = TestSupport.FakeDocumentPath,
            AllowRawHtml = true,
        });

        return Verifier.Verify(result.BodyHtml, extension: "html");
    }
}
