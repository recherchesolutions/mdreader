using System.IO;
using System.Windows;
using System.Windows.Interop;
using MdReader.Core;
using MdReader.Shell;

namespace MdReader.App;

// File-association integration: registration upkeep and the first-run
// suggestion bar (§4.3).
public partial class MainWindow
{
    private readonly FileAssociationRegistrar _registrar = new();

    private static string ExePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "mdreader.exe");

    private IReadOnlyCollection<string> RegisteredExtensions =>
        [.. FileTypes.DefaultExtensions, .. _settings.ExtraRegisteredExtensions];

    /// <summary>Keeps the per-user HKCU registration current (called off the UI thread).</summary>
    public void EnsureShellRegistration()
    {
        try
        {
            _registrar.Register(ExePath, RegisteredExtensions);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Services.DiagLog.Write($"shell registration failed: {ex.Message}");
        }

        Dispatcher.InvokeAsync(MaybeShowDefaultAppBar);
    }

    /// <summary>
    /// First run only (§4.3): show the non-modal bar when .md is not already
    /// pointing at us and the user hasn't dismissed it permanently.
    /// </summary>
    private void MaybeShowDefaultAppBar()
    {
        if (_settings.DontAskDefaultApp || _registrar.IsDefaultFor(".md"))
        {
            return;
        }

        DefaultAppBar.Visibility = Visibility.Visible;
    }

    private void HandleDefaultAppSet()
    {
        DefaultAppBar.Visibility = Visibility.Collapsed;

        // Primary path: the standard Windows "How do you want to open this
        // file?" dialog against the current document — the user makes the
        // choice, Windows records it.
        var document = ActiveDocument?.FilePath;
        if (document is not null)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (DefaultAppPrompt.ShowOpenWithDialog(hwnd, document))
            {
                return;
            }
        }

        // Fallback: Settings → Default apps, with text-only instructions.
        if (DefaultAppPrompt.OpenDefaultAppsSettings())
        {
            SetStatus(DefaultAppPrompt.SettingsInstructions);
        }
        else
        {
            SetStatus("Open Windows Settings → Apps → Default apps → mdreader to set the association.");
        }
    }

    private void HandleDefaultAppDontAsk()
    {
        _settings.DontAskDefaultApp = true;
        _settings.Save();
        DefaultAppBar.Visibility = Visibility.Collapsed;
    }

    /// <summary>Live read-out for the settings page: which app owns each extension now.</summary>
    public IReadOnlyDictionary<string, string> GetCurrentAssociationOwners()
    {
        var result = new Dictionary<string, string>();
        foreach (var extension in FileTypes.DefaultExtensions.Concat(FileTypes.OptionalExtensions))
        {
            result[extension] = _registrar.GetUserChoiceProgId(extension) ?? "(no user choice)";
        }

        return result;
    }
}
