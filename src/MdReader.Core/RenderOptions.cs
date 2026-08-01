namespace MdReader.Core;

/// <summary>Options for a single document render.</summary>
public sealed record RenderOptions
{
    /// <summary>Full path of the markdown file, used to resolve relative image paths. Null for unsaved/stdin content.</summary>
    public string? DocumentPath { get; init; }

    /// <summary>
    /// Load remote (http/https) images. Off by default: remote images are a
    /// read-receipt/tracking vector. When off, remote images render as an inert
    /// placeholder carrying the original URL in <c>data-remote-src</c>.
    /// </summary>
    public bool AllowRemoteImages { get; init; }

    /// <summary>
    /// Per-document opt-in for raw HTML passthrough (default off). When off, raw
    /// HTML in the document is escaped and shown as text. When on, it is rendered —
    /// and the sanitizer allowlist still runs on the output either way.
    /// </summary>
    public bool AllowRawHtml { get; init; }

    /// <summary>
    /// How many directory levels above the document's folder a relative image path
    /// may reach before it is refused. Default 3 per the security posture.
    /// </summary>
    public int MaxImagePathParentLevels { get; init; } = 3;

    /// <summary>
    /// Leave allowed relative image paths as-is instead of routing them through
    /// the WebView document virtual host. Used by the CLI/HTML export, where the
    /// output opens in a real browser next to the document. Policy checks
    /// (parent-traversal limits, remote blocking) still apply.
    /// </summary>
    public bool KeepRelativeImagePaths { get; init; }
}
