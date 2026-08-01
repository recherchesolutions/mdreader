namespace MdReader.App;

// Export, print, copy-as-rich-text, and the settings dialog: implemented in Phase 5.
public partial class MainWindow
{
    private Task ExportHtmlAsync()
    {
        SetStatus("Export to HTML is not implemented yet.");
        return Task.CompletedTask;
    }

    private Task ExportPdfAsync()
    {
        SetStatus("Export to PDF is not implemented yet.");
        return Task.CompletedTask;
    }

    private Task PrintActiveAsync()
    {
        SetStatus("Print is not implemented yet.");
        return Task.CompletedTask;
    }

    private Task CopyRichTextAsync()
    {
        SetStatus("Copy as rich text is not implemented yet.");
        return Task.CompletedTask;
    }

    private void ShowSettingsDialog()
    {
        SetStatus("Settings dialog is not implemented yet.");
    }
}
