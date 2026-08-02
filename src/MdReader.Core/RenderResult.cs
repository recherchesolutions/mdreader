namespace MdReader.Core;

/// <summary>The output of rendering one markdown document.</summary>
public sealed record RenderResult
{
    /// <summary>Sanitized HTML for the document body (front matter card + rendered markdown).</summary>
    public required string BodyHtml { get; init; }

    /// <summary>Headings for the TOC rail, in document order.</summary>
    public required IReadOnlyList<HeadingInfo> Headings { get; init; }

    /// <summary>Document title: first level-1 heading, else front matter title, else null.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// The directory the WebView2 document virtual host must map to so that the
    /// rewritten relative image URLs resolve. Null when the document has no path
    /// or references no local images.
    /// </summary>
    public string? DocumentRootPath { get; init; }

    public ReadingStats.Result ReadingStats { get; init; }

    public IReadOnlyList<DocumentDiagnostic> Diagnostics { get; init; } = [];
}
