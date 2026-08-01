using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MdReader.App.Services;

/// <summary>
/// Owns the shared WebView2 environment (warmed on a background thread during
/// startup per the §6 performance plan) and applies the §5.5 hardening to every
/// WebView the app creates.
/// </summary>
public static class WebViewFactory
{
    private static Task<CoreWebView2Environment>? _environmentTask;

    public static string WebAssetsPath =>
        Path.Combine(AppContext.BaseDirectory, "Web");

    /// <summary>Kick off environment creation early; called once at startup.</summary>
    public static Task<CoreWebView2Environment> WarmEnvironmentAsync()
    {
        var options = new CoreWebView2EnvironmentOptions();
        // Diagnostics escape hatch: MDREADER_BROWSER_ARGS passes extra Chromium
        // flags (e.g. --disable-gpu on GPU-less VMs where GPU probing costs
        // seconds at startup).
        if (Environment.GetEnvironmentVariable("MDREADER_BROWSER_ARGS") is { Length: > 0 } extraArgs)
        {
            options.AdditionalBrowserArguments = extraArgs;
        }

        return _environmentTask ??= CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: Path.Combine(AppSettings.SettingsDirectory, "webview2"),
            options: options);
    }

    /// <summary>
    /// Initializes a WebView with the shared environment, hardening, and the
    /// read-only assets virtual host mapping.
    /// </summary>
    public static async Task InitializeAsync(WebView2 webView)
    {
        var environment = await WarmEnvironmentAsync();
        await webView.EnsureCoreWebView2Async(environment);

        var core = webView.CoreWebView2;
        var settings = core.Settings;

        // §5.5 WebView2 hardening.
#if DEBUG
        settings.AreDevToolsEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
#endif
        settings.AreDefaultContextMenusEnabled = true; // customized per control below
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsWebMessageEnabled = true;
#if !DEBUG
        settings.AreHostObjectsAllowed = false;
#endif

        // Strip navigation-ish context menu entries (back/forward/reload/save),
        // keeping the text editing ones (copy, select all…).
        core.ContextMenuRequested += static (_, e) =>
        {
            for (var i = e.MenuItems.Count - 1; i >= 0; i--)
            {
                var name = e.MenuItems[i].Name;
                if (name is "back" or "forward" or "reload" or "saveAs" or "print"
                    or "share" or "webCapture" or "inspectElement" or "openLinkInNewWindow"
                    or "saveLinkAs" or "saveImageAs" or "openImageInNewWindow" or "magnifyImage")
                {
                    e.MenuItems.RemoveAt(i);
                }
            }
        };

        // Read-only mapping of the bundled web assets. Document folders get their
        // own mapping per document (see DocumentView).
        core.SetVirtualHostNameToFolderMapping(
            MdReader.Core.VirtualHosts.Assets,
            WebAssetsPath,
            CoreWebView2HostResourceAccessKind.Allow);

        // New windows (window.open, ctrl+click) are never allowed; route to the
        // same handling as normal link clicks instead.
        core.NewWindowRequested += static (_, e) => e.Handled = true;
    }
}
