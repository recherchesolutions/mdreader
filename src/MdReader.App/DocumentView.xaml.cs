using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MdReader.App.Services;
using MdReader.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MdReader.App;

public enum ViewMode
{
    Reader,
    Source,
    Split,
}

/// <summary>
/// One open document: a reader WebView, a lazily-created Monaco editor WebView,
/// live reload, dirty tracking, scroll-synchronized mode switching, and the
/// navigation security boundary.
/// </summary>
public partial class DocumentView : UserControl
{
    private static readonly MarkdownRenderer Renderer = new();
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly AppSettings _settings;
    private TextFileInfo? _file;
    private string _currentText = string.Empty;
    private bool _editorInitialized;
    private bool _editorReady;
    private bool _readerReady;
    private FileSystemWatcher? _watcher;
    private System.Windows.Threading.DispatcherTimer? _reloadDebounce;
    private bool _allowRemoteImagesThisDocument;
    private int _lastKnownLine = 1;
    private bool _suppressEditorDirty;
    private TaskCompletionSource<string>? _pendingContentRequest;
    private readonly List<string> _pendingEditorMessages = [];

    public string FilePath { get; private set; }
    public ViewMode Mode { get; private set; } = ViewMode.Reader;
    public bool IsDirty { get; private set; }
    public string? DocumentTitle { get; private set; }
    public bool TocEligible { get; private set; }
    public bool TocOpen { get; private set; }

    public event EventHandler? StateChanged;
    public event EventHandler<string>? OpenFileRequested;
    public event EventHandler<string>? ShortcutInvoked;
    public event EventHandler<(int total, int current)>? FindResultChanged;
    public event EventHandler<string>? StatusMessage;

    /// <summary>Created in code or adopted pre-warmed from <see cref="WebViewPool"/>.</summary>
    private WebView2 ReaderView = null!;

    public DocumentView(string filePath, AppSettings settings)
    {
        InitializeComponent();
        FilePath = Path.GetFullPath(filePath);
        _settings = settings;
        _allowRemoteImagesThisDocument = settings.LoadRemoteImages;
    }

    public string TabTitle => (IsDirty ? "• " : string.Empty) + Path.GetFileName(FilePath);

    /* ------------------------------------------------------------------ *
     * Initialization
     * ------------------------------------------------------------------ */
    public async Task InitializeAsync(ViewMode initialMode)
    {
        DiagLog.Write($"InitializeAsync start: {FilePath}");
        _file = TextFileIO.Read(FilePath);
        _currentText = _file.Text;

        var pooled = WebViewPool.TryTake();
        bool pageAlreadyLoaded;
        if (pooled is { } warm)
        {
            DiagLog.Write($"adopting pre-warmed reader (pageReady={warm.PageReady})");
            ReaderView = warm.WebView;
            pageAlreadyLoaded = true;
            _readerReady = warm.PageReady;
        }
        else
        {
            DiagLog.Write("initializing reader webview (cold)");
            ReaderView = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Transparent };
            pageAlreadyLoaded = false;
        }

        Grid.SetColumn(ReaderView, 2);
        ViewGrid.Children.Add(ReaderView);

        if (!pageAlreadyLoaded)
        {
            await WebViewFactory.InitializeAsync(ReaderView);
            DiagLog.Write("reader webview initialized");
        }

        ConfigureReaderCore(ReaderView.CoreWebView2);
        ApplyZoom(_settings.Zoom);

        if (_allowRemoteImagesThisDocument || !pageAlreadyLoaded)
        {
            // Needs its own navigation (remote-image CSP variant, or cold path).
            NavigateReader();
        }
        else if (_readerReady)
        {
            // Adopted page finished loading while in the pool: skip the wait
            // and drive it directly.
            OnReaderPageReady();
        }
        // else: the adopted page is still loading; its 'ready' message arrives
        // through the handler wired in ConfigureReaderCore.

        StartWatcher();

        if (initialMode != ViewMode.Reader)
        {
            await SetModeAsync(initialMode);
        }
    }

    private void ConfigureReaderCore(CoreWebView2 core)
    {
        // Map the document's allowed root (doc dir + up to 3 parents) read-only.
        var docDir = Path.GetDirectoryName(FilePath)!;
        var root = ImagePathRewriter.ComputeAllowedRoot(docDir, new RenderOptions().MaxImagePathParentLevels);
        core.SetVirtualHostNameToFolderMapping(
            VirtualHosts.Document, root, CoreWebView2HostResourceAccessKind.Allow);

        core.NavigationStarting += OnReaderNavigationStarting;
        core.WebMessageReceived += OnReaderMessage;
        // Drops land on the window (which opens files) rather than the page.
        ReaderView.AllowExternalDrop = false;

        if (DiagLog.Enabled)
        {
            core.NavigationCompleted += (_, e) =>
                DiagLog.Write($"reader navigation completed: success={e.IsSuccess} status={e.WebErrorStatus}");
            core.ProcessFailed += (_, e) => DiagLog.Write($"reader process failed: {e.ProcessFailedKind}");
            core.DOMContentLoaded += (_, _) => DiagLog.Write("reader DOMContentLoaded");
        }
    }

    private void NavigateReader()
    {
        _readerReady = false;
        var page = _allowRemoteImagesThisDocument ? "reader-remote.html" : "reader.html";
        ReaderView.CoreWebView2.Navigate($"{VirtualHosts.AssetsOrigin}/{page}");
    }

    /* ------------------------------------------------------------------ *
     * The navigation security boundary (§5.4): the WebView never navigates
     * away from the app's own pages. Everything else is cancelled and routed.
     * ------------------------------------------------------------------ */
    private static readonly string[] AllowedReaderPages =
    [
        $"{VirtualHosts.AssetsOrigin}/reader.html",
        $"{VirtualHosts.AssetsOrigin}/reader-remote.html",
    ];

    private void OnReaderNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (AllowedReaderPages.Contains(e.Uri, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        Dispatcher.InvokeAsync(() => HandleExternalUri(e.Uri));
    }

    private void OnEditorNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!string.Equals(e.Uri, $"{VirtualHosts.AssetsOrigin}/editor.html", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
        }
    }

    private void HandleExternalUri(string uri)
    {
        if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                StatusMessage?.Invoke(this, $"Could not open link: {uri}");
            }

            return;
        }

        // file:, ms-*, custom protocols: refused with a visible notice (§5.4).
        StatusMessage?.Invoke(this, $"Refused to open link with unsupported scheme: {Truncate(uri, 120)}");
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    /* ------------------------------------------------------------------ *
     * Messages from reader.js
     * ------------------------------------------------------------------ */
    private void OnReaderMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        DiagLog.Write($"reader msg: {Truncate(e.WebMessageAsJson, 120)}");
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

        switch (type)
        {
            case "ready":
                _readerReady = true;
                OnReaderPageReady();
                break;

            case "bodyRendered":
                _bodyRendered?.TrySetResult();
                break;

            case "link":
                HandleDocumentLink(root.GetProperty("href").GetString() ?? string.Empty);
                break;

            case "scrollChanged":
                if (Mode is ViewMode.Reader or ViewMode.Split)
                {
                    _lastKnownLine = root.GetProperty("line").GetInt32();
                    if (Mode == ViewMode.Split)
                    {
                        SyncScroll(toEditor: true, _lastKnownLine);
                    }
                }

                break;

            case "scrollLine":
                _lastKnownLine = root.GetProperty("line").GetInt32();
                break;

            case "tocEligibility":
                TocEligible = root.GetProperty("eligible").GetBoolean();
                StateChanged?.Invoke(this, EventArgs.Empty);
                break;

            case "findResult":
                FindResultChanged?.Invoke(this, (
                    root.GetProperty("total").GetInt32(),
                    root.GetProperty("current").GetInt32()));
                break;

            case "requestRemoteImages":
                AllowRemoteImagesForDocument();
                break;

            case "shortcut":
                ShortcutInvoked?.Invoke(this, root.GetProperty("name").GetString() ?? string.Empty);
                break;
        }
    }

    /// <summary>Runs once the reader page is live (its own 'ready' or pool adoption).</summary>
    private void OnReaderPageReady()
    {
        PostReader(new { type = "setTheme", theme = EffectiveTheme() });
        PostFontOverrides();
        PostCustomTheme();
        _ = RenderToReaderAsync(preserveScroll: false, scrollToLine: _lastKnownLine);
    }

    private void HandleDocumentLink(string href)
    {
        if (href.Length == 0)
        {
            return;
        }

        if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            HandleExternalUri(href);
            return;
        }

        if (href.Contains("://", StringComparison.Ordinal) || href.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            HandleExternalUri(href);
            return;
        }

        // Relative link: resolve against the document folder. Markdown files open
        // in a new tab; anything else is refused (the shell can be asked later).
        try
        {
            var target = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(FilePath)!,
                Uri.UnescapeDataString(href.Split('#')[0].Split('?')[0]).Replace('/', Path.DirectorySeparatorChar)));

            if (FileTypes.IsMarkdown(target) && File.Exists(target))
            {
                OpenFileRequested?.Invoke(this, target);
            }
            else
            {
                StatusMessage?.Invoke(this, File.Exists(target)
                    ? $"Refused to open non-markdown file: {Path.GetFileName(target)}"
                    : $"File not found: {Truncate(target, 120)}");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            StatusMessage?.Invoke(this, "Refused invalid link path.");
        }
    }

    /* ------------------------------------------------------------------ *
     * Rendering
     * ------------------------------------------------------------------ */
    private async Task RenderToReaderAsync(bool preserveScroll, int? scrollToLine = null)
    {
        if (!_readerReady)
        {
            return;
        }

        var text = _currentText;
        var options = new RenderOptions
        {
            DocumentPath = FilePath,
            AllowRemoteImages = _allowRemoteImagesThisDocument,
        };

        var largeDoc = text.Length > 1_500_000;
        var result = await Task.Run(() => Renderer.Render(text, options));

        DocumentTitle = result.Title;
        DiagLog.Write($"render complete: {result.BodyHtml.Length} chars, {result.Headings.Count} headings");
        PostReader(new
        {
            type = "setBody",
            html = result.BodyHtml,
            headings = result.Headings.Select(h => new { level = h.Level, text = h.Text, id = h.Id, line = h.SourceLine }),
            largeDoc,
            preserveScroll,
            scrollToLine,
        });
        StateChanged?.Invoke(this, EventArgs.Empty);

        // Diagnostics: MDREADER_SHOT=<path.png> captures the rendered reader.
        if (Environment.GetEnvironmentVariable("MDREADER_SHOT") is { } shotPath)
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(2500); // let hljs/mermaid/katex finish
                try
                {
                    await using var stream = File.Create(shotPath);
                    await ReaderView.CoreWebView2.CapturePreviewAsync(
                        CoreWebView2CapturePreviewImageFormat.Png, stream);
                    DiagLog.Write($"capture written: {shotPath}");
                }
                catch (Exception ex)
                {
                    DiagLog.Write($"capture failed: {ex.Message}");
                }
            });
        }
    }

    private void PostReader(object message)
    {
        if (ReaderView.CoreWebView2 is { } core)
        {
            core.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonOpts));
        }
    }

    private void PostEditor(object message)
    {
        var json = JsonSerializer.Serialize(message, JsonOpts);
        if (_editorReady && EditorView.CoreWebView2 is { } core)
        {
            core.PostWebMessageAsJson(json);
        }
        else
        {
            _pendingEditorMessages.Add(json);
        }
    }

    private string EffectiveTheme() => WindowsTheme.Resolve(_settings.Theme);

    private void PostCustomTheme()
    {
        var css = _settings.CustomTheme is { } name ? ThemeLoader.ReadCustomTheme(name) : null;
        PostReader(new { type = "setCustomCss", css = css ?? string.Empty });
    }

    private void PostFontOverrides()
    {
        PostReader(new
        {
            type = "setFont",
            family = _settings.FontFamilyOverride,
            size = _settings.FontSizeOverride,
            contentWidth = _settings.ContentWidthOverride,
        });
    }

    /* ------------------------------------------------------------------ *
     * Editor (Monaco) — created lazily on first switch to Source/Split
     * ------------------------------------------------------------------ */
    private async Task EnsureEditorAsync()
    {
        if (_editorInitialized)
        {
            return;
        }

        _editorInitialized = true;
        await WebViewFactory.InitializeAsync(EditorView);
        var core = EditorView.CoreWebView2;
        core.NavigationStarting += OnEditorNavigationStarting;
        core.WebMessageReceived += OnEditorMessage;
        EditorView.AllowExternalDrop = false;
        EditorView.ZoomFactor = _settings.Zoom;
        core.Navigate($"{VirtualHosts.AssetsOrigin}/editor.html");
    }

    private void OnEditorMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

        switch (type)
        {
            case "ready":
                _editorReady = true;
                DiagLog.Write("editor ready");
                PostEditorPending();
                PostEditor(new { type = "setTheme", theme = EffectiveTheme() });

                if (Environment.GetEnvironmentVariable("MDREADER_SHOT_EDITOR") is { } editorShot)
                {
                    _ = Dispatcher.InvokeAsync(async () =>
                    {
                        await Task.Delay(2500);
                        try
                        {
                            await using var stream = File.Create(editorShot);
                            await EditorView.CoreWebView2.CapturePreviewAsync(
                                CoreWebView2CapturePreviewImageFormat.Png, stream);
                            DiagLog.Write($"editor capture written: {editorShot}");
                        }
                        catch (Exception ex)
                        {
                            DiagLog.Write($"editor capture failed: {ex.Message}");
                        }
                    });
                }

                break;

            case "contentChanged":
                if (!_suppressEditorDirty && !IsDirty)
                {
                    IsDirty = true;
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
                else if (_suppressEditorDirty)
                {
                    // ignored: programmatic set
                }

                break;

            case "content":
                _pendingContentRequest?.TrySetResult(root.GetProperty("text").GetString() ?? string.Empty);
                break;

            case "scrollChanged":
                if (Mode is ViewMode.Source or ViewMode.Split)
                {
                    _lastKnownLine = root.GetProperty("line").GetInt32();
                    if (Mode == ViewMode.Split)
                    {
                        SyncScroll(toEditor: false, _lastKnownLine);
                    }
                }

                break;

            case "scrollLine":
                _lastKnownLine = root.GetProperty("line").GetInt32();
                break;

            case "saveRequested":
                _ = SaveAsync();
                break;

            case "toggleModeRequested":
                ShortcutInvoked?.Invoke(this, "toggleMode");
                break;

            case "shortcut":
                ShortcutInvoked?.Invoke(this, root.GetProperty("name").GetString() ?? string.Empty);
                break;
        }
    }

    private void PostEditorPending()
    {
        foreach (var json in _pendingEditorMessages)
        {
            EditorView.CoreWebView2.PostWebMessageAsJson(json);
        }

        _pendingEditorMessages.Clear();
    }

    private async Task<string> RequestEditorContentAsync()
    {
        if (!_editorReady)
        {
            return _currentText;
        }

        _pendingContentRequest = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostEditor(new { type = "requestContent" });
        var completed = await Task.WhenAny(_pendingContentRequest.Task, Task.Delay(3000));
        return completed == _pendingContentRequest.Task ? _pendingContentRequest.Task.Result : _currentText;
    }

    /* ------------------------------------------------------------------ *
     * Mode switching with scroll preservation
     * ------------------------------------------------------------------ */
    public async Task SetModeAsync(ViewMode mode)
    {
        if (mode == Mode)
        {
            return;
        }

        // Leaving Source: pull the buffer so Reader reflects unsaved edits.
        if (Mode is ViewMode.Source or ViewMode.Split && _editorReady)
        {
            _currentText = await RequestEditorContentAsync();
        }

        Mode = mode;

        if (mode is ViewMode.Source or ViewMode.Split)
        {
            await EnsureEditorAsync();
            _suppressEditorDirty = true;
            PostEditor(new
            {
                type = "setContent",
                text = _currentText,
                eol = _file is null ? "\r\n" : TextFileIO.DominantEol(_file),
                line = _lastKnownLine,
            });
            _suppressEditorDirty = false;
        }

        ApplyLayout();

        if (mode is ViewMode.Reader or ViewMode.Split)
        {
            await RenderToReaderAsync(preserveScroll: false, scrollToLine: _lastKnownLine);
        }

        if (mode == ViewMode.Source)
        {
            PostEditor(new { type = "scrollToLine", line = _lastKnownLine });
            PostEditor(new { type = "focus" });
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task ToggleModeAsync() =>
        SetModeAsync(Mode == ViewMode.Reader ? ViewMode.Source : ViewMode.Reader);

    public Task ToggleSplitAsync() =>
        SetModeAsync(Mode == ViewMode.Split ? ViewMode.Reader : ViewMode.Split);

    private void ApplyLayout()
    {
        switch (Mode)
        {
            case ViewMode.Reader:
                EditorColumn.Width = new GridLength(0);
                SplitterColumn.Width = new GridLength(0);
                ReaderColumn.Width = new GridLength(1, GridUnitType.Star);
                EditorView.Visibility = Visibility.Collapsed;
                Splitter.Visibility = Visibility.Collapsed;
                ReaderView.Visibility = Visibility.Visible;
                break;

            case ViewMode.Source:
                EditorColumn.Width = new GridLength(1, GridUnitType.Star);
                SplitterColumn.Width = new GridLength(0);
                ReaderColumn.Width = new GridLength(0);
                EditorView.Visibility = Visibility.Visible;
                Splitter.Visibility = Visibility.Collapsed;
                ReaderView.Visibility = Visibility.Collapsed;
                break;

            case ViewMode.Split:
                EditorColumn.Width = new GridLength(1, GridUnitType.Star);
                SplitterColumn.Width = GridLength.Auto;
                ReaderColumn.Width = new GridLength(1, GridUnitType.Star);
                EditorView.Visibility = Visibility.Visible;
                Splitter.Visibility = Visibility.Visible;
                ReaderView.Visibility = Visibility.Visible;
                break;
        }
    }

    private bool _syncingScroll;

    private void SyncScroll(bool toEditor, int line)
    {
        if (_syncingScroll)
        {
            return;
        }

        _syncingScroll = true;
        try
        {
            if (toEditor)
            {
                PostEditor(new { type = "scrollToLine", line });
            }
            else
            {
                PostReader(new { type = "scrollToLine", line });
            }
        }
        finally
        {
            // Release after a short delay so the echoed scrollChanged is ignored.
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(200);
                _syncingScroll = false;
            });
        }
    }

    /* ------------------------------------------------------------------ *
     * Saving
     * ------------------------------------------------------------------ */
    public async Task<bool> SaveAsync()
    {
        if (_file is null)
        {
            return false;
        }

        if (Mode is ViewMode.Source or ViewMode.Split)
        {
            _currentText = await RequestEditorContentAsync();
        }

        var text = ApplyLineEndingPolicy(_currentText);

        try
        {
            SuspendWatcher();
            TextFileIO.Write(FilePath, _file, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage?.Invoke(this, $"Save failed: {ex.Message}");
            ResumeWatcher();
            return false;
        }

        _file = TextFileIO.Read(FilePath);
        _currentText = _file.Text;
        IsDirty = false;
        ResumeWatcher();
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (Mode is ViewMode.Reader or ViewMode.Split)
        {
            await RenderToReaderAsync(preserveScroll: true);
        }

        StatusMessage?.Invoke(this, $"Saved {Path.GetFileName(FilePath)}");
        return true;
    }

    private string ApplyLineEndingPolicy(string text) => _settings.LineEndingPolicy switch
    {
        LineEndingPolicy.Crlf => text.ReplaceLineEndings("\r\n"),
        LineEndingPolicy.Lf => text.ReplaceLineEndings("\n"),
        _ => text, // Preserve: the editor already uses the file's dominant EOL
    };

    /* ------------------------------------------------------------------ *
     * Live reload (250ms debounce; editors and generators write in bursts)
     * ------------------------------------------------------------------ */
    private void StartWatcher()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (dir is null || !Directory.Exists(dir))
        {
            return;
        }

        _watcher = new FileSystemWatcher(dir, Path.GetFileName(FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };

        _reloadDebounce = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _reloadDebounce.Tick += (_, _) =>
        {
            _reloadDebounce!.Stop();
            OnFileChangedDebounced();
        };

        FileSystemEventHandler onChanged = (_, _) => Dispatcher.InvokeAsync(() =>
        {
            _reloadDebounce!.Stop();
            _reloadDebounce.Start();
        });

        _watcher.Changed += onChanged;
        _watcher.Created += onChanged;
        _watcher.Renamed += (_, e) => Dispatcher.InvokeAsync(() =>
        {
            if (string.Equals(e.FullPath, FilePath, StringComparison.OrdinalIgnoreCase))
            {
                _reloadDebounce!.Stop();
                _reloadDebounce.Start();
            }
        });

        _watcher.EnableRaisingEvents = true;
    }

    private void SuspendWatcher()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
        }
    }

    private void ResumeWatcher()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = true;
        }
    }

    private void OnFileChangedDebounced()
    {
        if (!File.Exists(FilePath))
        {
            return; // deleted or renamed away; keep the buffer
        }

        if (IsDirty)
        {
            ReloadBar.Visibility = Visibility.Visible;
            return;
        }

        ReloadFromDisk();
    }

    private void ReloadFromDisk()
    {
        try
        {
            _file = TextFileIO.Read(FilePath);
        }
        catch (IOException)
        {
            // Writer still holds the file; the next change event will retry.
            return;
        }

        _currentText = _file.Text;
        IsDirty = false;
        ReloadBar.Visibility = Visibility.Collapsed;
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (Mode is ViewMode.Source or ViewMode.Split)
        {
            _suppressEditorDirty = true;
            PostEditor(new
            {
                type = "setContent",
                text = _currentText,
                eol = TextFileIO.DominantEol(_file),
                line = _lastKnownLine,
            });
            _suppressEditorDirty = false;
        }

        if (Mode is ViewMode.Reader or ViewMode.Split)
        {
            _ = RenderToReaderAsync(preserveScroll: true);
        }
    }

    private void OnReloadClick(object sender, RoutedEventArgs e) => ReloadFromDisk();

    private void OnKeepMineClick(object sender, RoutedEventArgs e)
    {
        ReloadBar.Visibility = Visibility.Collapsed;
    }

    /* ------------------------------------------------------------------ *
     * Find, TOC, theme, zoom, remote images
     * ------------------------------------------------------------------ */
    public void FindStart(string query, bool matchCase)
    {
        if (Mode == ViewMode.Source)
        {
            PostEditor(new { type = "find" });
        }
        else
        {
            PostReader(new { type = "find", action = "start", query, matchCase });
        }
    }

    public void FindNext() => PostReader(new { type = "find", action = "next" });
    public void FindPrev() => PostReader(new { type = "find", action = "prev" });
    public void FindClear() => PostReader(new { type = "find", action = "clear" });

    /// <summary>Ctrl+F in Source mode uses Monaco's native find.</summary>
    public bool UsesNativeFind => Mode == ViewMode.Source;

    public void ToggleToc()
    {
        TocOpen = !TocOpen && TocEligible;
        PostReader(new { type = "setToc", open = TocOpen });
    }

    public void ApplyTheme()
    {
        PostReader(new { type = "setTheme", theme = EffectiveTheme() });
        PostEditor(new { type = "setTheme", theme = EffectiveTheme() });
    }

    public void ApplyZoom(double zoom)
    {
        ReaderView.ZoomFactor = zoom;
        if (_editorInitialized)
        {
            EditorView.ZoomFactor = zoom;
        }
    }

    private void AllowRemoteImagesForDocument()
    {
        _allowRemoteImagesThisDocument = true;
        // CSP is baked into the page at parse time, so switch to the variant
        // whose img-src permits http/https, then re-render.
        NavigateReader();
    }

    public async Task RefreshFromSettingsAsync()
    {
        _allowRemoteImagesThisDocument = _settings.LoadRemoteImages || _allowRemoteImagesThisDocument;
        ApplyTheme();
        PostFontOverrides();
        PostCustomTheme();
        ApplyZoom(_settings.Zoom);
        await RenderToReaderAsync(preserveScroll: true);
    }

    /* ------------------------------------------------------------------ *
     * Export / print (Phase 5)
     * ------------------------------------------------------------------ */
    private TaskCompletionSource? _bodyRendered;

    /// <summary>Re-renders and waits until mermaid/katex/hljs enhancement completed.</summary>
    public async Task<bool> WaitForFullRenderAsync(TimeSpan timeout)
    {
        _bodyRendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RenderToReaderAsync(preserveScroll: true);
        var completed = await Task.WhenAny(_bodyRendered.Task, Task.Delay(timeout));
        return completed == _bodyRendered.Task;
    }

    /// <summary>The reader's current rendered body DOM (post-enhancement).</summary>
    public async Task<string> GetRenderedBodyHtmlAsync()
    {
        FindClear();
        var json = await ReaderView.CoreWebView2.ExecuteScriptAsync(
            "document.getElementById('content').innerHTML");
        return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
    }

    public string EffectiveThemeName => EffectiveTheme();

    /// <summary>The current buffer text (markdown source).</summary>
    public string CurrentText => _currentText;

    public string? CustomThemeCss
    {
        get
        {
            var css = _settings.CustomTheme is { } name ? ThemeLoader.ReadCustomTheme(name) : null;
            if (_settings.ContentWidthOverride is { } width)
            {
                css = (css ?? string.Empty) + $"\n:root {{ --content-width: {width}px; }}\n";
            }

            return css;
        }
    }

    public Task ExportPdfAsync(string outputPath) =>
        ReaderView.CoreWebView2.PrintToPdfAsync(outputPath, null);

    public void ShowPrintDialog() =>
        ReaderView.CoreWebView2.ShowPrintUI(Microsoft.Web.WebView2.Core.CoreWebView2PrintDialogKind.Browser);

    /* ------------------------------------------------------------------ *
     * Teardown
     * ------------------------------------------------------------------ */
    public void Shutdown()
    {
        _watcher?.Dispose();
        _watcher = null;
        ReaderView.Dispose();
        EditorView.Dispose();
    }
}
