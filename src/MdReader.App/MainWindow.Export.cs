using System.IO;
using MdReader.App.Services;
using Microsoft.Win32;

namespace MdReader.App;

// Export, print, copy-as-rich-text, and the settings dialog (§3.5, §3.7).
public partial class MainWindow
{
    private async Task ExportHtmlAsync()
    {
        if (ActiveDocument is not { } doc)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "HTML file (*.html)|*.html",
            FileName = Path.GetFileNameWithoutExtension(doc.FilePath) + ".html",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SetStatus("Exporting to HTML…");
        try
        {
            await doc.WaitForFullRenderAsync(TimeSpan.FromSeconds(30));
            var body = await doc.GetRenderedBodyHtmlAsync();
            var html = ExportService.BuildSelfContainedHtml(
                body, doc.EffectiveThemeName, embedImages: true,
                title: doc.DocumentTitle ?? Path.GetFileNameWithoutExtension(doc.FilePath),
                customThemeCss: doc.CustomThemeCss,
                preset: _settings.ExportPreset);
            await File.WriteAllTextAsync(dialog.FileName, html);
            SetStatus($"Exported {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Export failed: {ex.Message}");
        }
    }

    private async Task ExportPdfAsync()
    {
        if (ActiveDocument is not { } doc)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PDF file (*.pdf)|*.pdf",
            FileName = Path.GetFileNameWithoutExtension(doc.FilePath) + ".pdf",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SetStatus("Exporting to PDF…");
        try
        {
            await doc.WaitForFullRenderAsync(TimeSpan.FromSeconds(30));
            await doc.ExportPdfAsync(dialog.FileName, _settings.ExportPreset);
            SetStatus($"Exported {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetStatus($"Export failed: {ex.Message}");
        }
    }

    private Task PrintActiveAsync()
    {
        ActiveDocument?.ShowPrintDialog();
        return Task.CompletedTask;
    }

    private async Task CopyRichTextAsync()
    {
        if (ActiveDocument is not { } doc)
        {
            return;
        }

        SetStatus("Copying…");
        var body = await doc.GetRenderedBodyHtmlAsync();
        var fragment = ExportService.BuildClipboardFragment(body);
        var css = ExportService.BuildInlineCss(doc.CustomThemeCss);

        ClipboardHtml.SetHtml(fragment, plainTextFallback: doc.CurrentText, inlineCss: css);
        SetStatus("Copied document as rich text.");
    }

    private async void ShowSettingsDialog()
    {
        var dialog = new SettingsWindow(_settings, GetCurrentAssociationOwners()) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings.Save();
            UpdateThemeChecks();
            UpdateZoomStatus();
            await Task.Run(EnsureShellRegistration);
            foreach (var doc in AllDocuments)
            {
                await doc.RefreshFromSettingsAsync();
            }
        }

        if (dialog.SetDefaultAppRequested)
        {
            HandleDefaultAppSet();
        }
    }
}
