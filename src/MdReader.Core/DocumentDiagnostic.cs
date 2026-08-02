using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MdReader.Core;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record DocumentDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string? DocumentPath,
    int Line,
    string? Target = null);

/// <summary>Local-only document validation shared by the GUI and CLI.</summary>
public static class MarkdownDiagnostics
{
    public static IReadOnlyList<DocumentDiagnostic> Analyze(string markdown, string? documentPath)
    {
        var document = Markdown.Parse(markdown, MarkdownPipelineFactory.Safe);
        HeadingIdAssigner.Assign(document);
        return AnalyzeDocument(document, markdown, documentPath, TocBuilder.Collect(document));
    }

    internal static IReadOnlyList<DocumentDiagnostic> AnalyzeDocument(
        Markdig.Syntax.MarkdownDocument document,
        string markdown,
        string? documentPath,
        IReadOnlyList<HeadingInfo> headingInfo)
    {
        var result = new List<DocumentDiagnostic>();
        var headings = new HashSet<string>(headingInfo.Select(h => h.Id), StringComparer.OrdinalIgnoreCase);
        var baseDirectory = documentPath is null ? null : Path.GetDirectoryName(Path.GetFullPath(documentPath));

        foreach (var link in document.Descendants<LinkInline>())
        {
            var target = link.Url;
            if (string.IsNullOrWhiteSpace(target) || IsRemote(target))
            {
                continue;
            }

            var line = LineFromOffset(markdown, Math.Max(0, link.Span.Start));
            var fragmentAt = target.IndexOf('#');
            var pathPart = fragmentAt >= 0 ? target[..fragmentAt] : target;
            var fragment = fragmentAt >= 0 ? SafeUnescape(target[(fragmentAt + 1)..]) : null;

            if (pathPart.Length == 0)
            {
                if (fragment is { Length: > 0 } && !headings.Contains(fragment))
                {
                    result.Add(new("MD002", DiagnosticSeverity.Warning,
                        $"Heading '#{fragment}' does not exist.", documentPath, line, target));
                }

                continue;
            }

            if (baseDirectory is null)
            {
                continue;
            }

            try
            {
                var decoded = Uri.UnescapeDataString(pathPart).Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathRooted(decoded))
                {
                    result.Add(new("MD003", DiagnosticSeverity.Warning,
                        "Absolute local paths are not portable.", documentPath, line, target));
                    continue;
                }

                var resolved = Path.GetFullPath(Path.Combine(baseDirectory, decoded));
                if (!File.Exists(resolved))
                {
                    result.Add(new(link.IsImage ? "MD001" : "MD004", DiagnosticSeverity.Warning,
                        link.IsImage ? "Local image was not found." : "Local linked file was not found.",
                        documentPath, line, resolved));
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UriFormatException)
            {
                result.Add(new("MD005", DiagnosticSeverity.Warning,
                    "Local target has an invalid path.", documentPath, line, target));
            }
        }

        return result;
    }

    private static bool IsRemote(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" or "mailto";

    private static string? SafeUnescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static int LineFromOffset(string text, int offset)
    {
        var line = 1;
        for (var i = 0; i < Math.Min(offset, text.Length); i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
