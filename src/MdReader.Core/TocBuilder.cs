using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MdReader.Core;

/// <summary>Collects headings (with their auto-generated anchor ids) for the TOC rail.</summary>
public static class TocBuilder
{
    public static IReadOnlyList<HeadingInfo> Collect(MarkdownDocument document)
    {
        var headings = new List<HeadingInfo>();
        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            var id = heading.GetAttributes().Id ?? string.Empty;
            headings.Add(new HeadingInfo(heading.Level, ExtractText(heading), id, heading.Line + 1));
        }

        return headings;
    }

    private static string ExtractText(HeadingBlock heading)
    {
        if (heading.Inline is null)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        foreach (var inline in heading.Inline.Descendants())
        {
            switch (inline)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case LineBreakInline:
                    sb.Append(' ');
                    break;
            }
        }

        return sb.ToString().Trim();
    }
}
