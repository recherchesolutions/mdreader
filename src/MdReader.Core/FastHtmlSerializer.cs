using System.Text;
using AngleSharp.Dom;

namespace MdReader.Core;

/// <summary>
/// Minimal, fast HTML serializer for sanitized DOM fragments.
/// AngleSharp's own ToHtml/InnerHtml serialization measures ~96s on an 8MB
/// document (vs ~0.3s here), which is what made 5MB markdown files unusable.
/// This serializer is safe for our output because sanitized fragments contain
/// no script/style raw-text elements, no comments worth keeping, and no
/// foreign (SVG/MathML) content — every text node can be uniformly encoded.
/// </summary>
public static class FastHtmlSerializer
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.Ordinal)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "source", "track", "wbr",
    };

    public static string SerializeChildren(IElement parent, int capacityHint = 4096)
    {
        var sb = new StringBuilder(capacityHint);
        foreach (var node in parent.ChildNodes)
        {
            Serialize(node, sb);
        }

        return sb.ToString();
    }

    private static void Serialize(INode node, StringBuilder sb)
    {
        switch (node)
        {
            case IText text:
                AppendTextEncoded(sb, text.Data);
                break;

            case IElement el:
                sb.Append('<').Append(el.LocalName);
                foreach (var attr in el.Attributes)
                {
                    sb.Append(' ').Append(attr.Name).Append("=\"");
                    AppendAttributeEncoded(sb, attr.Value);
                    sb.Append('"');
                }

                sb.Append('>');
                if (!VoidElements.Contains(el.LocalName))
                {
                    foreach (var child in el.ChildNodes)
                    {
                        Serialize(child, sb);
                    }

                    sb.Append("</").Append(el.LocalName).Append('>');
                }

                break;

                // Comments and other node types are intentionally dropped.
        }
    }

    private static void AppendTextEncoded(StringBuilder sb, string text)
    {
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '\u00A0': sb.Append("&nbsp;"); break;
                default: sb.Append(c); break;
            }
        }
    }

    private static void AppendAttributeEncoded(StringBuilder sb, string text)
    {
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\u00A0': sb.Append("&nbsp;"); break;
                default: sb.Append(c); break;
            }
        }
    }
}
