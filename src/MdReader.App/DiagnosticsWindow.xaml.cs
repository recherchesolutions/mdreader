using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MdReader.Core;

namespace MdReader.App;

public partial class DiagnosticsWindow : Window
{
    public int? TargetLine { get; private set; }

    public DiagnosticsWindow(IReadOnlyList<DocumentDiagnostic> diagnostics)
    {
        InitializeComponent();
        Summary.Text = diagnostics.Count == 0
            ? "No local link or image problems were found. Remote URLs are never checked automatically."
            : $"{diagnostics.Count} local issue(s). Double-click an item to jump to its source line.";
        foreach (var diagnostic in diagnostics)
        {
            DiagnosticList.Items.Add(new ListBoxItem
            {
                Content = $"Line {diagnostic.Line}: {diagnostic.Message}  [{diagnostic.Code}]",
                ToolTip = diagnostic.Target,
                Tag = diagnostic.Line,
            });
        }
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DiagnosticList.SelectedItem is ListBoxItem { Tag: int line })
        {
            TargetLine = line;
            DialogResult = true;
        }
    }
}
