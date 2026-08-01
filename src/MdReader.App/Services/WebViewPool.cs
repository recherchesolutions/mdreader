using System.Windows.Controls;
using MdReader.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MdReader.App.Services;

/// <summary>
/// Keeps one reader WebView pre-initialized and pre-navigated in a hidden host
/// so opening a tab only has to post content instead of paying WebView2
/// creation (~1s) plus page load (~2s). This is what makes the warm-start
/// (single-instance handoff → new tab) target reachable.
/// </summary>
public static class WebViewPool
{
    private sealed class WarmEntry
    {
        public required WebView2 WebView { get; init; }
        public bool Ready;
        public required EventHandler<CoreWebView2WebMessageReceivedEventArgs> ReadyHandler { get; init; }
    }

    private static Panel? _host;
    private static WarmEntry? _warm;
    private static bool _warming;

    /// <summary>Called once after the main window loads; starts warming.</summary>
    public static void Attach(Panel host)
    {
        _host = host;
        _ = WarmNextAsync();
    }

    /// <summary>
    /// Takes the pre-warmed reader WebView if one is available. The caller owns
    /// it from here (including disposal); warming of the next one begins
    /// immediately. Returns null when the pool is empty or not attached.
    /// </summary>
    public static (WebView2 WebView, bool PageReady)? TryTake()
    {
        if (_warm is null || _host is null)
        {
            return null;
        }

        var entry = _warm;
        _warm = null;

        // Detach the warm-phase ready listener so this pool never reacts to the
        // adopted view's later messages.
        entry.WebView.CoreWebView2.WebMessageReceived -= entry.ReadyHandler;

        _host.Children.Remove(entry.WebView);
        entry.WebView.Width = double.NaN;  // undo the 0x0 warming size
        entry.WebView.Height = double.NaN;
        entry.WebView.IsHitTestVisible = true;

        _ = WarmNextAsync();
        return (entry.WebView, entry.Ready);
    }

    private static async Task WarmNextAsync()
    {
        if (_host is null || _warming || _warm is not null)
        {
            return;
        }

        _warming = true;
        try
        {
            var webView = new WebView2
            {
                Width = 0,
                Height = 0,
                IsHitTestVisible = false,
                Focusable = false,
                DefaultBackgroundColor = System.Drawing.Color.Transparent,
            };

            _host.Children.Add(webView);
            await WebViewFactory.InitializeAsync(webView);

            // Warm-phase navigation guard: only the reader pages may load.
            // DocumentView layers its own routing handler on adoption; both
            // cancelling is fine, and this one never routes anywhere.
            webView.CoreWebView2.NavigationStarting += static (_, e) =>
            {
                if (!e.Uri.StartsWith($"{VirtualHosts.AssetsOrigin}/reader", StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                }
            };

            WarmEntry entry = null!;
            EventHandler<CoreWebView2WebMessageReceivedEventArgs> readyHandler = (_, e) =>
            {
                // The only message the warm page sends is its ready handshake.
                if (e.WebMessageAsJson.Contains("\"ready\"", StringComparison.Ordinal))
                {
                    entry.Ready = true;
                }
            };

            entry = new WarmEntry { WebView = webView, ReadyHandler = readyHandler };
            webView.CoreWebView2.WebMessageReceived += readyHandler;
            webView.CoreWebView2.Navigate($"{VirtualHosts.AssetsOrigin}/reader.html");
            _warm = entry;
            DiagLog.Write("webview pool: warm reader prepared");
        }
        catch (Exception ex)
        {
            // Pool failures must never break opening documents — the slow path
            // (DocumentView creating its own WebView) always works.
            DiagLog.Write($"webview pool warm failed: {ex.Message}");
        }
        finally
        {
            _warming = false;
        }
    }
}
