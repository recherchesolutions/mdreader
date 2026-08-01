using Markdig;
using Markdig.Extensions.EmphasisExtras;

namespace MdReader.Core;

/// <summary>
/// Builds the single, canonical Markdig pipeline for mdreader.
/// Every markdown feature the app supports is enabled here and nowhere else,
/// so tests, the CLI, and the app all render identically.
/// </summary>
public static class MarkdownPipelineFactory
{
    private static readonly Lazy<MarkdownPipeline> CachedSafe = new(() => Build(allowRawHtml: false));
    private static readonly Lazy<MarkdownPipeline> CachedRawHtml = new(() => Build(allowRawHtml: true));

    /// <summary>
    /// Default pipeline: raw HTML in documents is escaped and displayed as text.
    /// Pipelines are expensive to build and thread-safe once built, so both
    /// variants are cached for the lifetime of the process.
    /// </summary>
    public static MarkdownPipeline Safe => CachedSafe.Value;

    /// <summary>
    /// Per-document opt-in pipeline: raw HTML passes through to the renderer.
    /// The sanitizer allowlist still runs on the output.
    /// </summary>
    public static MarkdownPipeline RawHtml => CachedRawHtml.Value;

    public static MarkdownPipeline Build(bool allowRawHtml)
    {
        var builder = new MarkdownPipelineBuilder();
        if (!allowRawHtml)
        {
            builder.DisableHtml();
        }

        return builder
            .UsePipeTables()
            .UseGridTables()
            .UseTaskLists()
            .UseEmphasisExtras(EmphasisExtraOptions.Default) // strikethrough, sub/superscript, inserted, marked
            .UseFootnotes()
            .UseDefinitionLists()
            // Heading anchor ids are assigned by HeadingIdAssigner (O(n)), not by
            // UseAutoIdentifiers, which is quadratic on duplicate-heavy documents.
            .UseAutoLinks()
            .UseCitations()
            .UseAbbreviations()
            .UseYamlFrontMatter()
            .UseEmojiAndSmiley()
            .UseCustomContainers()
            .UseMathematics()
            // Note: block-level Line numbers (all the source anchors need) are
            // tracked by Markdig unconditionally; UsePreciseSourceLocation is
            // inline-level and unnecessary here.
            // Generic attributes must be registered after the other inline parsers.
            .UseGenericAttributes()
            .Build();
    }
}
