using System.Windows;
using MdReader.App.Services;

namespace MdReader.App;

public partial class App : Application
{
    private AppSettings _settings = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = CommandLine.Parse(e.Args);

        if (args.ShowVersion)
        {
            // --version prints to an attached console (or a message box when
            // launched without one) and exits.
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "dev";
            if (!ConsoleInterop.TryWriteLine($"mdreader {version}"))
            {
                MessageBox.Show($"mdreader {version}", "mdreader", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            Shutdown(0);
            return;
        }

        DiagLog.Write($"startup: args=[{string.Join(" ", e.Args)}]");
        _settings = AppSettings.Load();
        WindowsTheme.StartListening();

        // §6: warm the WebView2 environment during startup. CreateAsync must be
        // initiated from the STA/UI thread (RPC_E_CHANGED_MODE otherwise); it is
        // asynchronous and does its heavy work out of process.
        _ = WebViewFactory.WarmEnvironmentAsync();

        var window = new MainWindow(_settings);
        MainWindow = window;
        window.Show();

        if (args.FilePath is not null)
        {
            _ = window.OpenFileAsync(args.FilePath, args.OpenInSource ? ViewMode.Source : null);
        }
    }
}
