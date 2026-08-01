using System.IO;

namespace MdReader.App.Services;

/// <summary>
/// Opt-in diagnostic logging: set MDREADER_LOG to a file path and the app
/// appends timestamped lines. Local file only — mdreader has no telemetry.
/// </summary>
public static class DiagLog
{
    private static readonly string? LogPath = Environment.GetEnvironmentVariable("MDREADER_LOG");
    private static readonly Lock Sync = new();

    public static bool Enabled => LogPath is not null;

    public static void Write(string message)
    {
        if (LogPath is null)
        {
            return;
        }

        try
        {
            lock (Sync)
            {
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Diagnostics must never break the app.
        }
    }
}
