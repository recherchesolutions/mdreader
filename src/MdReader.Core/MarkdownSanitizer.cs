using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Ganss.Xss;

namespace MdReader.Core;

/// <summary>
/// Sanitizes rendered markdown HTML before it ever reaches the WebView.
/// Every document is treated as untrusted input (repos, downloads, LLM output,
/// mail attachments), so this is an allowlist, not a blocklist: anything not
/// explicitly permitted here is stripped.
/// </summary>
public sealed class MarkdownSanitizer
{
    private readonly HtmlSanitizer _sanitizer;

    /// <summary>
    /// Tags mdreader's own renderer can legitimately produce, plus the small set
    /// of raw-HTML tags worth keeping in documents (details/summary, figure, kbd…).
    /// Notably absent: script, iframe, object, embed, form, style, link, meta, base.
    /// </summary>
    private static readonly string[] AllowedTags =
    [
        "a", "abbr", "b", "blockquote", "br", "caption", "cite", "code", "col",
        "colgroup", "dd", "del", "details", "div", "dl", "dt", "em", "figcaption",
        "figure", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "i", "img", "input",
        "ins", "kbd", "li", "mark", "ol", "p", "pre", "q", "s", "samp", "section",
        "small", "span", "strong", "sub", "summary", "sup", "table", "tbody", "td",
        "tfoot", "th", "thead", "tr", "u", "ul", "var", "wbr",
    ];

    /// <summary>
    /// Attributes the renderer emits: ids for heading/footnote anchors, classes for
    /// hljs/math/mermaid/task-list hooks, table cell alignment, checkbox state, and
    /// the data-source-line scroll anchors (via AllowDataAttributes).
    /// Event handlers (on*) are never allowed by construction.
    /// </summary>
    private static readonly string[] AllowedAttributes =
    [
        "alt", "checked", "class", "colspan", "dir", "disabled", "href", "id",
        "lang", "open", "rowspan", "src", "start", "title", "type",
    ];

    /// <summary>
    /// URL schemes permitted on href/src. "file" is included because the reader
    /// intentionally displays local images (CSP and host-side navigation
    /// interception prevent it being used for navigation). "data" is NOT allowed:
    /// data: URIs only appear in exported HTML, where mdreader generates them
    /// itself after sanitization.
    /// </summary>
    private static readonly string[] AllowedSchemes = ["http", "https", "mailto", "file"];

    public MarkdownSanitizer()
    {
        _sanitizer = new HtmlSanitizer(new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(AllowedTags, StringComparer.OrdinalIgnoreCase),
            AllowedAttributes = new HashSet<string>(AllowedAttributes, StringComparer.OrdinalIgnoreCase),
            AllowedSchemes = new HashSet<string>(AllowedSchemes, StringComparer.OrdinalIgnoreCase),
            // CRITICAL: attributes listed here are validated against AllowedSchemes.
            // HtmlSanitizerOptions defaults this to EMPTY when options are built
            // explicitly — omitting it would let javascript: URLs through.
            UriAttributes = new HashSet<string>(["href", "src"], StringComparer.OrdinalIgnoreCase),
            // Markdig only emits text-align (table cell alignment); nothing else is needed.
            AllowedCssProperties = new HashSet<string>(["text-align"], StringComparer.OrdinalIgnoreCase),
            AllowedAtRules = new HashSet<AngleSharp.Css.Dom.CssRuleType>(),
        });

        // data-source-line et al. Data attributes are inert without script access.
        _sanitizer.AllowDataAttributes = true;

        // Task-list checkboxes are the only <input> allowed to survive; any other
        // input (text field, button, or an input with its type stripped) is removed
        // outright so that form-like UI cannot be smuggled into a document.
        _sanitizer.PostProcessNode += static (_, e) =>
        {
            if (e.Node is IHtmlInputElement input)
            {
                var type = input.GetAttribute("type");
                if (!string.Equals(type, "checkbox", StringComparison.OrdinalIgnoreCase))
                {
                    input.Remove();
                }
                else
                {
                    // Reader mode renders checkboxes inert; export keeps them inert too.
                    input.SetAttribute("disabled", "disabled");
                }
            }
        };
    }

    public string Sanitize(string html) => _sanitizer.Sanitize(html);

    /// <summary>
    /// Sanitizes and returns the DOM instead of a string. The renderer works on
    /// the DOM directly (image policy pass) and serializes once with
    /// <see cref="FastHtmlSerializer"/> — Ganss's own string serialization path
    /// is ~20x slower than the sanitization itself on multi-megabyte documents.
    /// </summary>
    public IHtmlDocument SanitizeToDocument(string html) =>
        _sanitizer.SanitizeDom($"<html><body>{html}</body></html>");
}
