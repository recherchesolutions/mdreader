using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace MdReader.App;

public enum UnsavedChangesResult
{
    Cancel,
    SaveSelected,
    DiscardAll,
}

/// <summary>
/// One clear review of every unsaved document when closing a tab or the app:
/// save the checked ones, discard everything, or cancel. Edits are never
/// silently discarded.
/// </summary>
public partial class UnsavedChangesWindow : Window
{
    private readonly List<(CheckBox Box, DocumentView Doc)> _rows = [];

    public UnsavedChangesResult Result { get; private set; } = UnsavedChangesResult.Cancel;

    /// <summary>Documents the user checked for saving (valid when Result is SaveSelected).</summary>
    public IReadOnlyList<DocumentView> SelectedToSave =>
        _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Doc).ToList();

    /// <summary>Documents left unchecked (their changes get discarded on SaveSelected).</summary>
    public IReadOnlyList<DocumentView> UncheckedToDiscard =>
        _rows.Where(r => r.Box.IsChecked != true).Select(r => r.Doc).ToList();

    public UnsavedChangesWindow(IEnumerable<DocumentView> dirtyDocuments)
    {
        InitializeComponent();

        foreach (var doc in dirtyDocuments)
        {
            var box = new CheckBox
            {
                IsChecked = true,
                Margin = new Thickness(0, 3, 0, 3),
                Content = new TextBlock
                {
                    Text = $"{Path.GetFileName(doc.FilePath)}  —  {Path.GetDirectoryName(doc.FilePath)}",
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
                ToolTip = doc.FilePath,
            };
            _rows.Add((box, doc));
            DocumentList.Items.Add(box);
        }
    }

    private void OnSaveSelectedClick(object sender, RoutedEventArgs e)
    {
        Result = UnsavedChangesResult.SaveSelected;
        DialogResult = true;
    }

    private void OnDiscardAllClick(object sender, RoutedEventArgs e)
    {
        Result = UnsavedChangesResult.DiscardAll;
        DialogResult = true;
    }
}
