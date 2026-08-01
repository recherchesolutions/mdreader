using MdReader.App.Services;

namespace MdReader.App;

/// <summary>
/// Custom entry point so the cheap paths never pay WPF startup costs:
/// --version prints and exits, and a second-instance launch hands its file to
/// the running instance over the pipe *before* any Application object is
/// created — that's what keeps warm handoff fast.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var parsed = CommandLine.Parse(args);

        if (parsed.ShowVersion)
        {
            var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "dev";
            ConsoleInterop.TryWriteLine($"mdreader {version}");
            return;
        }

        if (!parsed.IsHeadlessExport && parsed.FilePath is not null &&
            SingleInstance.TryActivateExistingInstance(
                new SingleInstance.Activation(parsed.FilePath, parsed.OpenInSource)))
        {
            return; // handed off to the running instance
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
