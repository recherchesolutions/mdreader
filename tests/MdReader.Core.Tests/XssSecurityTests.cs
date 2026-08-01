using AngleSharp.Html.Parser;
using FluentAssertions;

namespace MdReader.Core.Tests;

/// <summary>
/// Security tests: every payload in the XSS corpus must come out inert, in both
/// the default (raw HTML escaped) and the opt-in (raw HTML rendered + sanitized)
/// pipelines. Assertions are structural — parse the output and inspect the DOM —
/// because escaped payload text legitimately contains strings like "onerror=".
/// </summary>
public class XssSecurityTests
{
    private static readonly MarkdownRenderer Renderer = new();
    private static readonly HtmlParser Parser = new();

    private static readonly string[] ForbiddenTags =
        ["script", "iframe", "object", "embed", "form", "style", "link", "meta", "base", "svg", "math", "template", "frame", "frameset", "applet"];

    private static readonly string[] ForbiddenSchemes = ["javascript:", "vbscript:", "data:"];

    public static TheoryData<string, bool> Cases()
    {
        var data = new TheoryData<string, bool>();
        var xssDir = Path.Combine(TestSupport.FixturesDirectory, "xss");
        foreach (var file in Directory.EnumerateFiles(xssDir, "*.md"))
        {
            var name = "xss/" + Path.GetFileName(file);
            data.Add(name, false);
            data.Add(name, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Corpus_payload_is_neutralized(string fixture, bool allowRawHtml)
    {
        var markdown = TestSupport.ReadFixture(fixture);
        var result = Renderer.Render(markdown, new RenderOptions
        {
            DocumentPath = TestSupport.FakeDocumentPath,
            AllowRawHtml = allowRawHtml,
        });

        AssertInert(result.BodyHtml);
    }

    [Fact]
    public void Inline_event_handler_smuggled_via_attribute_text_is_stripped()
    {
        var result = Renderer.Render("""<p title="x" onmouseover="alert(1)">hover</p>""",
            new RenderOptions { AllowRawHtml = true });
        AssertInert(result.BodyHtml);
    }

    [Fact]
    public void Nonchecbox_inputs_are_removed_entirely()
    {
        var result = Renderer.Render("<input type=\"text\" value=\"x\"> <input> <input type=\"submit\">",
            new RenderOptions { AllowRawHtml = true });

        using var doc = Parser.ParseDocument($"<body>{result.BodyHtml}</body>");
        doc.QuerySelectorAll("input").Should().BeEmpty();
    }

    [Fact]
    public void Task_list_checkboxes_survive_and_are_disabled()
    {
        var result = Renderer.Render("- [x] done\n- [ ] open\n");

        using var doc = Parser.ParseDocument($"<body>{result.BodyHtml}</body>");
        var inputs = doc.QuerySelectorAll("input");
        inputs.Should().HaveCount(2);
        inputs.Should().OnlyContain(i =>
            i.GetAttribute("type") == "checkbox" && i.HasAttribute("disabled"));
    }

    private static void AssertInert(string bodyHtml)
    {
        using var doc = Parser.ParseDocument($"<body>{bodyHtml}</body>");

        foreach (var element in doc.Body!.QuerySelectorAll("*"))
        {
            ForbiddenTags.Should().NotContain(element.LocalName,
                $"tag <{element.LocalName}> must never survive sanitization");

            foreach (var attr in element.Attributes)
            {
                // "open" (details) is the one legitimate attribute starting with "on-".
                if (attr.Name != "open")
                {
                    attr.Name.Should().NotStartWith("on",
                        $"event handler attribute '{attr.Name}' must never survive sanitization");
                }

                if (attr.Name is "href" or "src" or "action" or "formaction" or "xlink:href" or "data")
                {
                    var value = attr.Value.Trim().Replace("\t", "").Replace("\n", "").Replace("\r", "").Replace(" ", "");
                    foreach (var scheme in ForbiddenSchemes)
                    {
                        value.Should().NotStartWithEquivalentOf(scheme,
                            $"{attr.Name}='{attr.Value}' must not carry a {scheme} URI");
                    }
                }
            }
        }
    }
}
