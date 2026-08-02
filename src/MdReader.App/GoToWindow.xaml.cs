using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MdReader.Core;

namespace MdReader.App;

/// <summary>
/// Ctrl+G: jump to a source line (digits) or a heading (anything else filters
/// the heading list; Enter or double-click jumps; Escape cancels).
/// </summary>
public partial class GoToWindow : Window
{
    private readonly IReadOnlyList<HeadingInfo> _headings;
    private readonly int _maxLine;

    /// <summary>The chosen source line, or null when cancelled.</summary>
    public int? TargetLine { get; private set; }

    public GoToWindow(IReadOnlyList<HeadingInfo> headings, int maxLine)
    {
        _headings = headings;
        _maxLine = Math.Max(1, maxLine);
        InitializeComponent();
        RefreshList(string.Empty);
        Loaded += (_, _) => InputBox.Focus();
    }

    private void RefreshList(string filter)
    {
        HeadingList.Items.Clear();
        foreach (var heading in _headings)
        {
            if (filter.Length == 0 ||
                heading.Text.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                HeadingList.Items.Add(new ListBoxItem
                {
                    Content = $"{new string(' ', (heading.Level - 1) * 2)}{heading.Text}   (line {heading.SourceLine})",
                    Tag = heading.SourceLine,
                });
            }
        }

        if (HeadingList.Items.Count > 0)
        {
            HeadingList.SelectedIndex = 0;
        }
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        var text = InputBox.Text.Trim();
        if (text.Length > 0 && text.All(char.IsAsciiDigit))
        {
            HeadingList.Items.Clear();
            var valid = int.TryParse(text, out var line) && line >= 1;
            HintText.Text = valid
                ? $"Press Enter to go to line {Math.Min(int.Parse(text), _maxLine)}."
                : "Enter a line number of 1 or greater.";
        }
        else
        {
            HintText.Text = "Type a line number, or text to filter headings.";
            RefreshList(text);
        }
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Accept();
                e.Handled = true;
                break;
            case Key.Down when HeadingList.Items.Count > 0:
                HeadingList.SelectedIndex = Math.Min(HeadingList.SelectedIndex + 1, HeadingList.Items.Count - 1);
                HeadingList.ScrollIntoView(HeadingList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when HeadingList.Items.Count > 0:
                HeadingList.SelectedIndex = Math.Max(HeadingList.SelectedIndex - 1, 0);
                HeadingList.ScrollIntoView(HeadingList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Escape:
                DialogResult = false;
                e.Handled = true;
                break;
        }
    }

    private void OnHeadingDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Accept();

    private void Accept()
    {
        var text = InputBox.Text.Trim();
        if (text.Length > 0 && text.All(char.IsAsciiDigit))
        {
            if (int.TryParse(text, out var line) && line >= 1)
            {
                // Out-of-range lines clamp to the end rather than erroring.
                TargetLine = Math.Min(line, _maxLine);
                DialogResult = true;
            }

            return; // invalid number: keep the dialog open, hint explains
        }

        if (HeadingList.SelectedItem is ListBoxItem { Tag: int headingLine })
        {
            TargetLine = headingLine;
            DialogResult = true;
        }
        // Empty input with no headings: nothing to accept; dialog stays open.
    }
}
