using System.Net;

namespace MdReader.Core;

/// <summary>
/// Assembles complete, standalone HTML documents (export, CLI conversion, and
/// browser previews of fixtures). The interactive reader does NOT use this: it
/// loads the static reader.html from the assets virtual host and swaps only the
/// body on re-render.
/// </summary>
public static class HtmlDocumentAssembler
{
    /// <summary>
    /// Content Security Policy for standalone documents. Scripts are impossible
    /// (default-src 'none', no script-src), inline styles are required because the
    /// stylesheet is inlined into the exported file, and data: images are allowed
    /// because export embeds images as data URIs that mdreader generated itself.
    /// </summary>
    public const string ExportCsp =
        "default-src 'none'; style-src 'unsafe-inline'; img-src file: data: http: https:";

    public static string BuildStandalone(RenderResult result, string inlineCss, string? title = null, string themeAttribute = "light")
    {
        var documentTitle = WebUtility.HtmlEncode(title ?? result.Title ?? "Markdown document");

        return $"""
            <!DOCTYPE html>
            <html lang="en" data-theme="{WebUtility.HtmlEncode(themeAttribute)}">
            <head>
            <meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="{ExportCsp}">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="generator" content="mdreader">
            <title>{documentTitle}</title>
            <style>
            {inlineCss}
            </style>
            </head>
            <body>
            <main class="content">
            {result.BodyHtml}
            </main>
            </body>
            </html>
            """;
    }
}
