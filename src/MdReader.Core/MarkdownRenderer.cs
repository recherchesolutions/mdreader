using System.Diagnostics;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;

namespace MdReader.Core;

/// <summary>
/// The one entry point for turning markdown text into safe, display-ready HTML:
/// parse → source-line anchors → render → sanitize → image policy → metadata card.
/// </summary>
public sealed class MarkdownRenderer
{
    private readonly MarkdownSanitizer _sanitizer = new();

    public RenderResult Render(string markdown, RenderOptions? options = null)
    {
        options ??= new RenderOptions();

        var pipeline = options.AllowRawHtml ? MarkdownPipelineFactory.RawHtml : MarkdownPipelineFactory.Safe;
        var timer = Stopwatch.StartNew();
        var phaseStart = timer.Elapsed;
        void Mark(string phase)
        {
            var now = timer.Elapsed;
            options.TimingSink?.Invoke(phase, now - phaseStart);
            phaseStart = now;
        }

        var document = Markdown.Parse(markdown, pipeline);
        Mark("parse");

        SourceLineAnchors.Apply(document);
        HeadingIdAssigner.Assign(document);
        var headings = TocBuilder.Collect(document);
        var readingStats = ReadingStats.CountDocument(document);
        var diagnostics = MarkdownDiagnostics.AnalyzeDocument(document, markdown, options.DocumentPath, headings);
        Mark("analyze");

        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        var rawHtml = writer.ToString();
        Mark("renderHtml");

        // One DOM pass: sanitize, apply the image policy in place, then a single
        // fast serialization (AngleSharp's own serializer is prohibitively slow
        // on large documents).
        using var dom = _sanitizer.SanitizeToDocument(rawHtml);
        Mark("sanitize");
        var body = dom.Body!;
        var documentRoot = ImagePathRewriter.Rewrite(body, options);
        Mark("rewriteImages");
        var bodyHtml = FastHtmlSerializer.SerializeChildren(body, rawHtml.Length + 1024);
        Mark("serialize");

        var frontMatterCard = FrontMatter.RenderCard(document, markdown);
        if (frontMatterCard.Length > 0)
        {
            bodyHtml = frontMatterCard + bodyHtml;
        }
        Mark("frontMatter");

        return new RenderResult
        {
            BodyHtml = bodyHtml,
            Headings = headings,
            Title = headings.FirstOrDefault(h => h.Level == 1)?.Text,
            DocumentRootPath = documentRoot,
            ReadingStats = readingStats,
            Diagnostics = diagnostics,
        };
    }
}
