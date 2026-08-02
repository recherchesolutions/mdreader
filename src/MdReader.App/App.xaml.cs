using System.Windows;
using MdReader.App.Services;

namespace MdReader.App;

public partial class App : Application
{
    private AppSettings _settings = null!;
    private SingleInstance? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --version and the fast second-instance handoff are handled in
        // Program.Main before any Application exists.
        var args = CommandLine.Parse(e.Args);

        DiagLog.Write($"startup: args=[{string.Join(" ", e.Args)}]");
        _settings = AppSettings.Load();

        // Headless exports run standalone: no window, no single-instance handoff.
        if (args.IsHeadlessExport)
        {
            _ = RunHeadlessExportAsync(args);
            return;
        }

        // Single instance with tabs: hand the file to a running instance if any.
        _singleInstance = new SingleInstance();
        var activation = new SingleInstance.Activation(args.FilePath, args.OpenInSource);
        if (!_singleInstance.TryBecomeOwner(activation))
        {
            Shutdown(0);
            return;
        }

        WindowsTheme.StartListening();

        // §6: warm the WebView2 environment during startup. CreateAsync must be
        // initiated from the STA/UI thread (RPC_E_CHANGED_MODE otherwise); it is
        // asynchronous and does its heavy work out of process.
        _ = WebViewFactory.WarmEnvironmentAsync();

        var window = new MainWindow(_settings);
        MainWindow = window;

        _singleInstance.Activated += (_, act) => Dispatcher.InvokeAsync(async () =>
        {
            DiagLog.Write($"activation received: {act.FilePath}");
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
            if (act.FilePath is not null)
            {
                await window.OpenFileAsync(act.FilePath, act.OpenInSource ? ViewMode.Source : null);
            }
        });

        window.Show();

        if (args.FilePath is not null)
        {
            _ = window.OpenFileAsync(args.FilePath, args.OpenInSource ? ViewMode.Source : null);
        }
        else if (_settings.RestorePreviousSession)
        {
            _ = window.RestoreSessionAsync();
        }
        else if (!_settings.HasSeenWelcomeDocument)
        {
            _settings.HasSeenWelcomeDocument = true;
            _settings.Save();
            _ = window.OpenWelcomeDocumentAsync();
        }

        // Crash recovery: offer any unsaved-buffer snapshots from a previous
        // session once the window is up.
        _ = Dispatcher.InvokeAsync(async () => await window.OfferRecoveryAsync(),
            System.Windows.Threading.DispatcherPriority.Background);

        // Keep the per-user registration fresh (HKCU only, additive, idempotent).
        _ = Task.Run(window.EnsureShellRegistration);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private async Task RunHeadlessExportAsync(CommandLine args)
    {
        var exitCode = await HeadlessExport.RunAsync(args);
        Shutdown(exitCode);
    }
}
