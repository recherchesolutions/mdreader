using MdReader.Core;

// mdreader-convert: dependency-light headless markdown → HTML conversion using
// the exact same pipeline and sanitizer as the app. Because no browser runs
// here, mermaid blocks stay as code, math stays as TeX, and fenced code is not
// colorized — for full-fidelity export use `mdreader <file> --export-html`.

if (args.Length is 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("""
        mdreader-convert — markdown to HTML

        usage:
          mdreader-convert <input.md> [output.html] [--theme light|dark] [--allow-raw-html]

        Writes a standalone, sanitized HTML document. When output is omitted,
        writes next to the input with an .html extension.
        """);
    return args.Length is 0 ? 1 : 0;
}

if (args[0] is "--version")
{
    Console.WriteLine($"mdreader-convert {typeof(MarkdownRenderer).Assembly.GetName().Version?.ToString(3)}");
    return 0;
}

var input = args[0];
if (!File.Exists(input))
{
    Console.Error.WriteLine($"mdreader-convert: file not found: {input}");
    return 2;
}

var output = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal)
    ? args[1]
    : Path.ChangeExtension(input, ".html");
var theme = args.Contains("--theme") && Array.IndexOf(args, "--theme") + 1 < args.Length
    ? args[Array.IndexOf(args, "--theme") + 1]
    : "light";
var allowRawHtml = args.Contains("--allow-raw-html");

try
{
    var info = TextFileIO.Read(input);
    var renderer = new MarkdownRenderer();
    var result = renderer.Render(info.Text, new RenderOptions
    {
        DocumentPath = Path.GetFullPath(input),
        AllowRawHtml = allowRawHtml,
        // No WebView here: images keep their original relative paths, which
        // stay valid when the HTML sits next to the document.
        KeepRelativeImagePaths = true,
    });

    var cssPath = Path.Combine(AppContext.BaseDirectory, "Web", "css", "reader.css");
    var css = File.Exists(cssPath) ? File.ReadAllText(cssPath) : string.Empty;

    var html = HtmlDocumentAssembler.BuildStandalone(
        result,
        css,
        result.Title ?? Path.GetFileNameWithoutExtension(input),
        theme == "dark" ? "dark" : "light");

    File.WriteAllText(output, html);
    Console.WriteLine($"wrote {output}");
    return 0;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"mdreader-convert: {ex.Message}");
    return 1;
}
