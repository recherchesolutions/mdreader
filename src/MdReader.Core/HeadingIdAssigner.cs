using Markdig.Helpers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MdReader.Core;

/// <summary>
/// Assigns GitHub-style anchor ids to headings in O(n).
/// This replaces Markdig's AutoIdentifiers extension, which degrades
/// quadratically on documents with tens of thousands of duplicate headings
/// (measured: ~8s per MB on a heading-heavy 1MB document, ~270s at 5MB).
/// Explicit ids from the generic-attributes extension ({#custom-id}) win.
/// </summary>
public static class HeadingIdAssigner
{
    public static void Assign(MarkdownDocument document)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            var attributes = heading.GetAttributes();
            if (!string.IsNullOrEmpty(attributes.Id))
            {
                // Explicit {#id}: keep it, but register it so a later automatic
                // id never collides with it.
                seen.TryAdd(attributes.Id, 0);
                continue;
            }

            var baseId = LinkHelper.UrilizeAsGfm(ExtractText(heading));
            if (string.IsNullOrEmpty(baseId))
            {
                baseId = "section";
            }

            if (seen.TryGetValue(baseId, out var count))
            {
                count++;
                seen[baseId] = count;
                attributes.Id = $"{baseId}-{count}";
            }
            else
            {
                seen[baseId] = 0;
                attributes.Id = baseId;
            }
        }
    }

    /// <summary>Plain text of a heading's inline content (shared with the TOC builder).</summary>
    internal static string ExtractText(HeadingBlock heading)
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
