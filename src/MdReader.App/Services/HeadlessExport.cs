using System.IO;
using System.Windows;

namespace MdReader.App.Services;

/// <summary>
/// Headless --export-html / --export-pdf (§3.6): renders the document in an
/// off-screen window (never shown in the taskbar, positioned outside the
/// desktop) so mermaid/katex/hljs run exactly as they do interactively, then
/// writes the output and exits with a CI-friendly exit code.
/// </summary>
public static class HeadlessExport
{
    public static async Task<int> RunAsync(CommandLine args)
    {
        if (args.FilePath is null || !File.Exists(args.FilePath))
        {
            ConsoleInterop.TryWriteLine($"mdreader: file not found: {args.FilePath}");
            return 2;
        }

        var settings = AppSettings.Load();

        // Off-screen host window: no taskbar entry, no activation, parked
        // outside the visible desktop.
        var window = new Window
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
            Left = -32000,
            Top = -32000,
            Width = 1100,
            Height = 1400,
        };

        var view = new DocumentView(args.FilePath, settings);
        window.Content = view;

        try
        {
            window.Show();
            await view.InitializeAsync(ViewMode.Reader);

            if (!await view.WaitForFullRenderAsync(TimeSpan.FromSeconds(60)))
            {
                ConsoleInterop.TryWriteLine("mdreader: render timed out");
                return 3;
            }

            if (args.ExportHtmlPath is { } htmlPath)
            {
                var body = await view.GetRenderedBodyHtmlAsync();
                var html = ExportService.BuildSelfContainedHtml(
                    body, view.EffectiveThemeName, embedImages: true,
                    title: view.DocumentTitle ?? Path.GetFileNameWithoutExtension(args.FilePath),
                    customThemeCss: view.CustomThemeCss);
                await File.WriteAllTextAsync(htmlPath, html);
                ConsoleInterop.TryWriteLine($"mdreader: wrote {htmlPath}");
            }

            if (args.ExportPdfPath is { } pdfPath)
            {
                await view.ExportPdfAsync(pdfPath);
                ConsoleInterop.TryWriteLine($"mdreader: wrote {pdfPath}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"headless export failed: {ex}");
            ConsoleInterop.TryWriteLine($"mdreader: export failed: {ex.Message}");
            return 1;
        }
        finally
        {
            view.Shutdown();
            window.Close();
        }
    }
}
