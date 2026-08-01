using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using MdReader.Core;

namespace MdReader.App.Services;

/// <summary>
/// Builds the self-contained HTML export: the reader's *rendered* DOM (mermaid
/// already SVG, math already KaTeX HTML, code already highlighted) plus inlined
/// CSS and optionally data-URI-embedded images. No scripts — the export CSP
/// forbids them and nothing dynamic remains.
/// </summary>
public static partial class ExportService
{
    /// <summary>Assembles the standalone document from rendered body HTML.</summary>
    public static string BuildSelfContainedHtml(string renderedBodyHtml, string theme, bool embedImages, string? title, string? customThemeCss)
    {
        var body = CleanBody(renderedBodyHtml, embedImages);
        var css = BuildInlineCss(customThemeCss);
        return HtmlDocumentAssembler.BuildStandalone(
            new RenderResult { BodyHtml = body, Headings = [] },
            css,
            title,
            theme);
    }

    /// <summary>reader.css + KaTeX CSS with fonts embedded + any custom theme.</summary>
    public static string BuildInlineCss(string? customThemeCss)
    {
        var webRoot = WebViewFactory.WebAssetsPath;
        var readerCss = File.ReadAllText(Path.Combine(webRoot, "css", "reader.css"));
        var katexCss = InlineKatexFonts(
            File.ReadAllText(Path.Combine(webRoot, "vendor", "katex", "katex.min.css")),
            Path.Combine(webRoot, "vendor", "katex"));

        var sb = new StringBuilder(readerCss.Length + katexCss.Length + 1024);
        sb.AppendLine(katexCss);
        sb.AppendLine(readerCss);
        if (!string.IsNullOrWhiteSpace(customThemeCss))
        {
            sb.AppendLine("/* custom theme */");
            sb.AppendLine(customThemeCss);
        }

        return sb.ToString();
    }

    /// <summary>Cleaned body fragment for the CF_HTML clipboard path.</summary>
    public static string BuildClipboardFragment(string renderedBodyHtml) =>
        CleanBody(renderedBodyHtml, embedImages: true);

    /// <summary>
    /// Removes reader-only chrome (copy buttons, find marks) and resolves
    /// mdreader-doc image URLs to data URIs or local file URLs.
    /// </summary>
    private static string CleanBody(string renderedBodyHtml, bool embedImages)
    {
        var parser = new HtmlParser();
        using var doc = parser.ParseDocument("<html><body></body></html>");
        var body = doc.Body!;
        body.InnerHtml = renderedBodyHtml;

        foreach (var button in body.QuerySelectorAll("button.copy-code").ToList())
        {
            button.Remove();
        }

        foreach (var mark in body.QuerySelectorAll("mark.find-hit").ToList())
        {
            var parent = mark.Parent;
            while (mark.FirstChild is { } child)
            {
                parent!.InsertBefore(child, mark);
            }

            mark.Remove();
        }

        foreach (var img in body.QuerySelectorAll("img[data-local-path]").ToList())
        {
            var localPath = img.GetAttribute("data-local-path")!;
            img.RemoveAttribute("data-local-path");

            if (embedImages && TryBuildDataUri(localPath, out var dataUri))
            {
                img.SetAttribute("src", dataUri);
            }
            else
            {
                img.SetAttribute("src", new Uri(localPath).AbsoluteUri);
            }
        }

        return FastHtmlSerializer.SerializeChildren(body, renderedBodyHtml.Length + 1024);
    }

    private static bool TryBuildDataUri(string path, out string dataUri)
    {
        dataUri = string.Empty;
        var mime = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".avif" => "image/avif",
            _ => null,
        };

        if (mime is null)
        {
            return false;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            dataUri = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Rewrites katex.min.css to carry its woff2 fonts as data URIs and drops
    /// the woff/ttf fallbacks (every browser that matters supports woff2).
    /// </summary>
    private static string InlineKatexFonts(string katexCss, string katexRoot)
    {
        // src:url(fonts/X.woff2) format("woff2"),url(fonts/X.woff) ...,url(fonts/X.ttf) ...
        katexCss = KatexSrcRegex().Replace(katexCss, match =>
        {
            var fontFile = match.Groups["file"].Value;
            var fontPath = Path.Combine(katexRoot, "fonts", fontFile);
            if (!File.Exists(fontPath))
            {
                return match.Value;
            }

            var b64 = Convert.ToBase64String(File.ReadAllBytes(fontPath));
            return $"src:url(data:font/woff2;base64,{b64}) format(\"woff2\")";
        });

        return katexCss;
    }

    [GeneratedRegex("""src:url\(fonts/(?<file>[^)]+\.woff2)\) format\("woff2"\)(,url\(fonts/[^)]+\) format\("[^"]+"\))*""")]
    private static partial Regex KatexSrcRegex();
}
