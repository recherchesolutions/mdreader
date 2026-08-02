using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using MdReader.App.Services;
using MdReader.Core;
using Microsoft.Win32;

namespace MdReader.App;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private bool _suppressFindChange;

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        RestoreWindowPlacement();
        UpdateThemeChecks();
        UpdateZoomStatus();
        RefreshRecentUi();
        UpdateCommandStates();

        WindowsTheme.SystemThemeChanged += (_, _) => Dispatcher.InvokeAsync(ApplyThemeToAllTabs);

        PreviewKeyDown += OnWindowKeyDown;
        Drop += OnFileDrop;
        DragOver += OnDragOver;
        Closing += OnWindowClosing;

        // Start pre-warming a reader WebView once the visual tree is live.
        Loaded += (_, _) => WebViewPool.Attach(WebViewWarmHost);
    }

    public DocumentView? ActiveDocument =>
        (Tabs.SelectedItem as TabItem)?.Content as DocumentView;

    private IEnumerable<DocumentView> AllDocuments =>
        Tabs.Items.OfType<TabItem>().Select(t => t.Content).OfType<DocumentView>();

    /* ------------------------------------------------------------------ *
     * Opening files and tabs
     * ------------------------------------------------------------------ */
    public async Task OpenFileAsync(string path, ViewMode? mode = null)
    {
        try
        {
            await OpenFileCoreAsync(path, mode);
        }
        catch (Exception ex)
        {
            Services.DiagLog.Write($"OpenFileAsync unhandled: {ex}");
            SetStatus($"Could not open {path}: {ex.Message}");
        }
    }

    private async Task OpenFileCoreAsync(string path, ViewMode? mode)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            SetStatus($"Invalid path: {path}");
            return;
        }

        if (!File.Exists(fullPath))
        {
            SetStatus($"File not found: {fullPath}");
            return;
        }

        // Already open? Activate its tab.
        foreach (TabItem item in Tabs.Items)
        {
            if (item.Content is DocumentView existing &&
                string.Equals(existing.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                Tabs.SelectedItem = item;
                return;
            }
        }

        var view = new DocumentView(fullPath, _settings);
        view.StateChanged += (_, _) => Dispatcher.InvokeAsync(() => OnDocumentStateChanged(view));
        view.OpenFileRequested += (_, target) => Dispatcher.InvokeAsync(() => _ = OpenFileAsync(target));
        view.ShortcutInvoked += (_, name) => Dispatcher.InvokeAsync(() => _ = HandleShortcutAsync(name));
        view.FindResultChanged += (_, r) => Dispatcher.InvokeAsync(() => FindCount.Text = r.total == 0 ? "No matches" : $"{r.current} of {r.total}");
        view.StatusMessage += (_, msg) => Dispatcher.InvokeAsync(() => SetStatus(msg));

        var tab = new TabItem
        {
            Header = view.TabTitle,
            Content = view,
        };

        Tabs.Items.Add(tab);
        Tabs.SelectedItem = tab;
        Tabs.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;

        _settings.TouchRecentFile(fullPath);
        _settings.Save();
        RefreshRecentUi();

        try
        {
            var initialMode = mode ?? (_settings.SplitViewByDefault
                ? ViewMode.Split
                : _settings.DefaultMode == DefaultMode.Source ? ViewMode.Source : ViewMode.Reader);
            await view.InitializeAsync(initialMode);
        }
        catch (Exception ex)
        {
            Services.DiagLog.Write($"OpenFileAsync failed: {ex}");
            SetStatus($"Could not open {fullPath}: {ex.Message}");
            Tabs.Items.Remove(tab);
            view.Shutdown();
            UpdateEmptyState();
            return;
        }

        UpdateWindowTitle();
        UpdateCommandStates();
    }

    private void OnDocumentStateChanged(DocumentView view)
    {
        foreach (TabItem item in Tabs.Items)
        {
            if (ReferenceEquals(item.Content, view))
            {
                item.Header = view.TabTitle;
            }
        }

        UpdateWindowTitle();
        UpdateCommandStates();
    }

    private async Task<bool> CloseTabAsync(TabItem tab)
    {
        if (tab.Content is not DocumentView view)
        {
            Tabs.Items.Remove(tab);
            return true;
        }

        if (view.IsDirty && !await ResolveUnsavedAsync([view]))
        {
            return false;
        }

        view.Shutdown();
        Tabs.Items.Remove(tab);
        UpdateEmptyState();
        UpdateWindowTitle();
        UpdateCommandStates();
        return true;
    }

    /// <summary>
    /// Presents the unsaved-changes review for the given dirty documents.
    /// Returns true when closing may proceed (everything saved or explicitly
    /// discarded), false when the user cancelled or a save failed.
    /// </summary>
    private async Task<bool> ResolveUnsavedAsync(IReadOnlyList<DocumentView> dirtyDocs)
    {
        var dialog = new UnsavedChangesWindow(dirtyDocs) { Owner = this };
        dialog.ShowDialog();

        switch (dialog.Result)
        {
            case UnsavedChangesResult.SaveSelected:
                foreach (var doc in dialog.SelectedToSave)
                {
                    if (!await doc.SaveAsync())
                    {
                        return false; // save failed: never discard silently
                    }
                }

                foreach (var doc in dialog.UncheckedToDiscard)
                {
                    doc.DiscardRecoverySnapshot();
                }

                return true;

            case UnsavedChangesResult.DiscardAll:
                foreach (var doc in dirtyDocs)
                {
                    doc.DiscardRecoverySnapshot();
                }

                return true;

            default:
                return false;
        }
    }

    private void UpdateEmptyState()
    {
        if (Tabs.Items.Count == 0)
        {
            Tabs.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            RefreshRecentUi();
        }
    }

    private void UpdateWindowTitle()
    {
        Title = ActiveDocument is { } doc
            ? $"{(doc.IsDirty ? "• " : "")}{Path.GetFileName(doc.FilePath)} — mdreader"
            : "mdreader";
    }

    private void UpdateCommandStates()
    {
        var doc = ActiveDocument;
        SaveMenuItem.IsEnabled = doc is not null;
        ExportMenu.IsEnabled = doc is not null;
        TocItem.IsEnabled = doc is { TocEligible: true };
        ReaderModeItem.IsChecked = doc is { Mode: ViewMode.Reader };
        SourceModeItem.IsChecked = doc is { Mode: ViewMode.Source };
        SplitModeItem.IsChecked = doc is { Mode: ViewMode.Split };
        ModeButton.Content = doc?.Mode.ToString() ?? "Reader";
    }

    /* ------------------------------------------------------------------ *
     * Shortcuts (routed from WebViews and from WPF key handling)
     * ------------------------------------------------------------------ */
    private async Task HandleShortcutAsync(string name)
    {
        switch (name)
        {
            case "toggleMode":
                if (ActiveDocument is { } d1) { await d1.ToggleModeAsync(); }
                break;
            case "toggleSplit":
                if (ActiveDocument is { } d2) { await d2.ToggleSplitAsync(); }
                break;
            case "toggleToc":
                ActiveDocument?.ToggleToc();
                break;
            case "goTo":
                ShowGoToDialog();
                break;
            case "navBack":
                ActiveDocument?.GoBack();
                break;
            case "navForward":
                ActiveDocument?.GoForward();
                break;
            case "openFile":
                ShowOpenDialog();
                break;
            case "find":
                ShowFindBar();
                break;
            case "save":
                if (ActiveDocument is { } d3) { await d3.SaveAsync(); }
                break;
            case "print":
                await PrintActiveAsync();
                break;
            case "closeTab":
                if (Tabs.SelectedItem is TabItem tab) { await CloseTabAsync(tab); }
                break;
            case "zoomIn":
                AdjustZoom(+0.1);
                break;
            case "zoomOut":
                AdjustZoom(-0.1);
                break;
            case "zoomReset":
                SetZoom(1.0);
                break;
        }

        UpdateCommandStates();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        // Alt+Left / Alt+Right: jump history (system key: read e.SystemKey).
        if (Keyboard.Modifiers == ModifierKeys.Alt)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Left or Key.Right)
            {
                e.Handled = true;
                _ = HandleShortcutAsync(key == Key.Left ? "navBack" : "navForward");
                return;
            }
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            string? name = e.Key switch
            {
                Key.E => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? "toggleSplit" : "toggleMode",
                Key.O when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) => "toggleToc",
                Key.O => "openFile",
                Key.F => "find",
                Key.G => "goTo",
                Key.S => "save",
                Key.P => "print",
                Key.W => "closeTab",
                Key.OemPlus or Key.Add => "zoomIn",
                Key.OemMinus or Key.Subtract => "zoomOut",
                Key.D0 or Key.NumPad0 => "zoomReset",
                Key.Tab => "nextTab",
                _ => null,
            };

            if (name == "nextTab")
            {
                if (Tabs.Items.Count > 1)
                {
                    Tabs.SelectedIndex = (Tabs.SelectedIndex + 1) % Tabs.Items.Count;
                }

                e.Handled = true;
                return;
            }

            if (name is not null)
            {
                e.Handled = true;
                _ = HandleShortcutAsync(name);
            }
        }
        else if (e.Key == Key.Escape && FindBar.Visibility == Visibility.Visible)
        {
            HideFindBar();
            e.Handled = true;
        }
    }

    /* ------------------------------------------------------------------ *
     * Find bar
     * ------------------------------------------------------------------ */
    private void ShowFindBar()
    {
        if (ActiveDocument is { UsesNativeFind: true } doc)
        {
            doc.FindStart(string.Empty, false); // triggers Monaco's native find
            return;
        }

        FindBar.Visibility = Visibility.Visible;
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void HideFindBar()
    {
        FindBar.Visibility = Visibility.Collapsed;
        FindCount.Text = string.Empty;
        ActiveDocument?.FindClear();
    }

    private void OnFindTextChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressFindChange)
        {
            return;
        }

        ActiveDocument?.FindStart(FindBox.Text, FindMatchCase.IsChecked == true);
    }

    private void OnFindBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                ActiveDocument?.FindPrev();
                e.Handled = true;
                break;
            case Key.Enter:
                ActiveDocument?.FindNext();
                e.Handled = true;
                break;
            case Key.Escape:
                HideFindBar();
                e.Handled = true;
                break;
        }
    }

    private void ShowGoToDialog()
    {
        if (ActiveDocument is not { } doc)
        {
            return;
        }

        var maxLine = doc.CurrentText.AsSpan().Count('\n') + 1;
        var dialog = new GoToWindow(doc.Headings, maxLine) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.TargetLine is { } line)
        {
            doc.JumpToLine(line);
        }
    }

    private void OnFindNextClick(object sender, RoutedEventArgs e) => ActiveDocument?.FindNext();
    private void OnFindPrevClick(object sender, RoutedEventArgs e) => ActiveDocument?.FindPrev();
    private void OnFindCloseClick(object sender, RoutedEventArgs e) => HideFindBar();

    /* ------------------------------------------------------------------ *
     * Zoom / theme
     * ------------------------------------------------------------------ */
    private void AdjustZoom(double delta) => SetZoom(Math.Clamp(_settings.Zoom + delta, 0.5, 3.0));

    private void SetZoom(double zoom)
    {
        _settings.Zoom = Math.Round(zoom, 2);
        _settings.Save();
        foreach (var doc in AllDocuments)
        {
            doc.ApplyZoom(_settings.Zoom);
        }

        UpdateZoomStatus();
    }

    private void UpdateZoomStatus() => ZoomStatus.Text = $"{Math.Round(_settings.Zoom * 100)}%";

    private void SetTheme(ThemeChoice choice)
    {
        _settings.Theme = choice;
        _settings.Save();
        UpdateThemeChecks();
        ApplyThemeToAllTabs();
    }

    private void ApplyThemeToAllTabs()
    {
        foreach (var doc in AllDocuments)
        {
            doc.ApplyTheme();
        }
    }

    private void UpdateThemeChecks()
    {
        ThemeSystemItem.IsChecked = _settings.Theme == ThemeChoice.System;
        ThemeLightItem.IsChecked = _settings.Theme == ThemeChoice.Light;
        ThemeDarkItem.IsChecked = _settings.Theme == ThemeChoice.Dark;
    }

    /* ------------------------------------------------------------------ *
     * Recent files
     * ------------------------------------------------------------------ */
    private void RefreshRecentUi()
    {
        RecentMenu.Items.Clear();
        RecentList.Items.Clear();

        var existing = _settings.RecentFiles.Where(File.Exists).ToList();
        RecentHeader.Visibility = existing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentMenu.IsEnabled = existing.Count > 0;

        foreach (var path in existing)
        {
            var menuItem = new MenuItem { Header = path };
            menuItem.Click += (_, _) => _ = OpenFileAsync(path);
            RecentMenu.Items.Add(menuItem);

            var link = new TextBlock { Margin = new Thickness(0, 2, 0, 2) };
            var hyperlink = new Hyperlink(new Run(path));
            hyperlink.Click += (_, _) => _ = OpenFileAsync(path);
            link.Inlines.Add(hyperlink);
            RecentList.Items.Add(link);
        }
    }

    /* ------------------------------------------------------------------ *
     * Drag & drop
     * ------------------------------------------------------------------ */
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files.Where(FileTypes.IsMarkdown))
            {
                _ = OpenFileAsync(file);
            }
        }
    }

    /* ------------------------------------------------------------------ *
     * Window persistence and close
     * ------------------------------------------------------------------ */
    private void RestoreWindowPlacement()
    {
        if (_settings.WindowLeft is { } left && _settings.WindowTop is { } top)
        {
            Left = left;
            Top = top;
        }

        Width = Math.Max(400, _settings.WindowWidth);
        Height = Math.Max(300, _settings.WindowHeight);
        if (_settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private bool _unsavedResolved;

    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var dirtyDocs = AllDocuments.Where(d => d.IsDirty).ToList();
        if (dirtyDocs.Count > 0 && !_unsavedResolved)
        {
            // One review of everything unsaved instead of a prompt per tab.
            e.Cancel = true;
            if (await ResolveUnsavedAsync(dirtyDocs))
            {
                _unsavedResolved = true;
                Close();
            }

            return;
        }

        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }

        _settings.Save();

        foreach (var doc in AllDocuments)
        {
            doc.Shutdown();
        }

        DocumentView.ScrollPositions.Save();
    }

    public void SetStatus(string message) => StatusText.Text = message;

    /* ------------------------------------------------------------------ *
     * Menu handlers
     * ------------------------------------------------------------------ */
    private void ShowOpenDialog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Markdown files (*.md;*.markdown;*.mdown;*.mkd;*.mkdn;*.mdtxt;*.mdtext;*.mdx)|*.md;*.markdown;*.mdown;*.mkd;*.mkdn;*.mdtxt;*.mdtext;*.mdx|All files (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            foreach (var file in dialog.FileNames)
            {
                _ = OpenFileAsync(file);
            }
        }
    }

    private void OnOpenClick(object sender, RoutedEventArgs e) => ShowOpenDialog();
    private async void OnSaveClick(object sender, RoutedEventArgs e) { if (ActiveDocument is { } d) { await d.SaveAsync(); } }
    private async void OnCloseTabClick(object sender, RoutedEventArgs e) { if (Tabs.SelectedItem is TabItem t) { await CloseTabAsync(t); } }
    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private async void OnReaderModeClick(object sender, RoutedEventArgs e) { if (ActiveDocument is { } d) { await d.SetModeAsync(ViewMode.Reader); UpdateCommandStates(); } }
    private async void OnSourceModeClick(object sender, RoutedEventArgs e) { if (ActiveDocument is { } d) { await d.SetModeAsync(ViewMode.Source); UpdateCommandStates(); } }
    private async void OnSplitModeClick(object sender, RoutedEventArgs e) { if (ActiveDocument is { } d) { await d.SetModeAsync(ViewMode.Split); UpdateCommandStates(); } }
    private async void OnModeButtonClick(object sender, RoutedEventArgs e) { if (ActiveDocument is { } d) { await d.ToggleModeAsync(); UpdateCommandStates(); } }
    private void OnTocClick(object sender, RoutedEventArgs e) => ActiveDocument?.ToggleToc();

    private void OnZoomInClick(object sender, RoutedEventArgs e) => AdjustZoom(+0.1);
    private void OnZoomOutClick(object sender, RoutedEventArgs e) => AdjustZoom(-0.1);
    private void OnZoomResetClick(object sender, RoutedEventArgs e) => SetZoom(1.0);

    private void OnThemeSystemClick(object sender, RoutedEventArgs e) => SetTheme(ThemeChoice.System);
    private void OnThemeLightClick(object sender, RoutedEventArgs e) => SetTheme(ThemeChoice.Light);
    private void OnThemeDarkClick(object sender, RoutedEventArgs e) => SetTheme(ThemeChoice.Dark);

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateWindowTitle();
        UpdateCommandStates();
        if (FindBar.Visibility == Visibility.Visible)
        {
            HideFindBar();
        }
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    // Implemented in MainWindow.Export.cs (Phase 5) and MainWindow.Shell.cs (Phase 4).
    private async void OnExportHtmlClick(object sender, RoutedEventArgs e) => await ExportHtmlAsync();
    private async void OnExportPdfClick(object sender, RoutedEventArgs e) => await ExportPdfAsync();
    private async void OnPrintClick(object sender, RoutedEventArgs e) => await PrintActiveAsync();
    private async void OnCopyRichTextClick(object sender, RoutedEventArgs e) => await CopyRichTextAsync();
    private void OnSettingsClick(object sender, RoutedEventArgs e) => ShowSettingsDialog();
    private void OnDefaultAppSetClick(object sender, RoutedEventArgs e) => HandleDefaultAppSet();
    private void OnDefaultAppNotNowClick(object sender, RoutedEventArgs e) => DefaultAppBar.Visibility = Visibility.Collapsed;
    private void OnDefaultAppDontAskClick(object sender, RoutedEventArgs e) => HandleDefaultAppDontAsk();
}
