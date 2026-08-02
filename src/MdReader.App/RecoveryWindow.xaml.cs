using System.IO;
using System.Windows;
using System.Windows.Controls;
using MdReader.App.Services;

namespace MdReader.App;

public enum RecoveryResult
{
    Later,
    RestoreSelected,
    DiscardAll,
}

/// <summary>Startup offer for crash-recovery snapshots: restore, discard, or decide later.</summary>
public partial class RecoveryWindow : Window
{
    private readonly List<(CheckBox Box, RecoveryEntry Entry)> _rows = [];

    public RecoveryResult Result { get; private set; } = RecoveryResult.Later;

    public IReadOnlyList<RecoveryEntry> SelectedEntries =>
        _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Entry).ToList();

    public RecoveryWindow(IReadOnlyList<RecoveryEntry> entries)
    {
        InitializeComponent();

        foreach (var entry in entries)
        {
            var missing = !File.Exists(entry.OriginalPath);
            var label = $"{Path.GetFileName(entry.OriginalPath)}  —  {entry.SavedAt.LocalDateTime:g}"
                + (missing ? "  (original file missing; a recovered copy will be created)" : string.Empty);

            var box = new CheckBox
            {
                IsChecked = !missing,
                Margin = new Thickness(0, 3, 0, 3),
                Content = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis },
                ToolTip = entry.OriginalPath,
            };
            _rows.Add((box, entry));
            EntryList.Items.Add(box);
        }
    }

    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        Result = RecoveryResult.RestoreSelected;
        DialogResult = true;
    }

    private void OnDiscardAllClick(object sender, RoutedEventArgs e)
    {
        Result = RecoveryResult.DiscardAll;
        DialogResult = true;
    }
}
