using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace MdReader.Core;

/// <summary>
/// Stamps every rendered block element with a <c>data-source-line</c> attribute
/// (1-based source line number). The reader script uses these anchors to map a
/// scroll position to a source line and back, which is what makes Ctrl+E mode
/// switches land on the same spot in the document.
/// </summary>
public static class SourceLineAnchors
{
    public const string AttributeName = "data-source-line";

    public static void Apply(MarkdownDocument document)
    {
        foreach (var block in document.Descendants<Block>())
        {
            // Front matter is rendered as a metadata card, not as markdown output.
            if (block is Markdig.Extensions.Yaml.YamlFrontMatterBlock)
            {
                continue;
            }

            // Container inlines are handled by their parent block; list containers
            // get anchors on their items instead, which are finer-grained targets.
            if (block is ListBlock)
            {
                continue;
            }

            var attributes = block.GetAttributes();
            attributes.AddProperty(AttributeName, (block.Line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
