namespace MdReader.App.Services;

/// <summary>Headless --export-html / --export-pdf (implemented in Phase 5).</summary>
public static class HeadlessExport
{
    public static Task<int> RunAsync(CommandLine args)
    {
        ConsoleInterop.TryWriteLine("Export is not implemented yet.");
        return Task.FromResult(1);
    }
}
