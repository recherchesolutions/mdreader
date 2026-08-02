using System.IO;
using MdReader.App.Services;
using MdReader.Core;

namespace MdReader.App;

// Startup crash-recovery offer (see RecoveryStore).
public partial class MainWindow
{
    /// <summary>Called once after startup; shows the recovery dialog when snapshots exist.</summary>
    public async Task OfferRecoveryAsync()
    {
        var entries = DocumentView.Recovery.ListAndPrune();
        if (entries.Count == 0)
        {
            return;
        }

        var dialog = new RecoveryWindow(entries) { Owner = this };
        dialog.ShowDialog();

        switch (dialog.Result)
        {
            case RecoveryResult.RestoreSelected:
                foreach (var entry in dialog.SelectedEntries)
                {
                    await RestoreEntryAsync(entry);
                }

                break;

            case RecoveryResult.DiscardAll:
                foreach (var entry in entries)
                {
                    DocumentView.Recovery.Remove(entry.OriginalPath);
                }

                break;

                // Later: keep everything for the next launch.
        }
    }

    private async Task RestoreEntryAsync(RecoveryEntry entry)
    {
        var path = entry.OriginalPath;

        if (!File.Exists(path))
        {
            // Original is gone: materialize the recovered text as a sibling of
            // where the file lived (or Documents when that folder is gone too),
            // then open that copy. Never guess at overwriting anything.
            var directory = Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir)
                ? dir
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var name = Path.GetFileNameWithoutExtension(path);
            var recoveredPath = Path.Combine(directory, $"{name}-recovered{Path.GetExtension(path)}");
            var counter = 1;
            while (File.Exists(recoveredPath))
            {
                recoveredPath = Path.Combine(directory, $"{name}-recovered-{counter++}{Path.GetExtension(path)}");
            }

            try
            {
                AtomicFileWriter.Write(recoveredPath, System.Text.Encoding.UTF8.GetBytes(entry.Text));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus($"Could not write recovered copy for {Path.GetFileName(path)}: {ex.Message}");
                return;
            }

            DocumentView.Recovery.Remove(path); // content now lives in a real file
            await OpenFileAsync(recoveredPath);
            return;
        }

        await OpenFileAsync(path);
        var view = AllDocuments.FirstOrDefault(d =>
            string.Equals(d.FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        if (view is not null)
        {
            // Buffer gets the recovered text, marked dirty; the snapshot stays
            // until the user saves (SaveAsync clears it) or discards.
            await view.RestoreRecoveredTextAsync(entry.Text);
            SetStatus($"Restored unsaved changes for {Path.GetFileName(path)} — review and save.");
        }
    }
}
