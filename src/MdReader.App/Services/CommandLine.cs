namespace MdReader.App.Services;

/// <summary>
/// Parsed command line per §3.6:
///   mdreader &lt;file.md&gt;                    open in reader mode
///   mdreader &lt;file.md&gt; --source           open in source mode
///   mdreader &lt;file.md&gt; --export-html out  headless HTML export
///   mdreader &lt;file.md&gt; --export-pdf out   headless PDF export
///   mdreader --version
/// </summary>
public sealed record CommandLine
{
    public string? FilePath { get; init; }
    public bool OpenInSource { get; init; }
    public string? ExportHtmlPath { get; init; }
    public string? ExportPdfPath { get; init; }
    public bool ShowVersion { get; init; }

    public bool IsHeadlessExport => ExportHtmlPath is not null || ExportPdfPath is not null;

    public static CommandLine Parse(string[] args)
    {
        string? file = null;
        var source = false;
        string? exportHtml = null;
        string? exportPdf = null;
        var version = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--version":
                    version = true;
                    break;
                case "--source":
                    source = true;
                    break;
                case "--export-html" when i + 1 < args.Length:
                    exportHtml = args[++i];
                    break;
                case "--export-pdf" when i + 1 < args.Length:
                    exportPdf = args[++i];
                    break;
                default:
                    file ??= args[i];
                    break;
            }
        }

        return new CommandLine
        {
            FilePath = file,
            OpenInSource = source,
            ExportHtmlPath = exportHtml,
            ExportPdfPath = exportPdf,
            ShowVersion = version,
        };
    }
}
