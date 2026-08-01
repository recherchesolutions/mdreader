using System.Diagnostics;

namespace MdReader.Integration.Tests;

/// <summary>
/// Launches the real mdreader.exe with diagnostic logging enabled and lets
/// tests await specific log lines. This exercises the actual app (WebView2,
/// message bridge, watchers) without depending on desktop focus.
/// </summary>
public sealed class AppHarness : IDisposable
{
    public Process Process { get; }
    public string LogPath { get; }

    public AppHarness(string arguments)
    {
        LogPath = Path.Combine(Path.GetTempPath(), $"mdreader-test-{Guid.NewGuid():N}.log");

        var psi = new ProcessStartInfo(ExePath, arguments)
        {
            UseShellExecute = false,
        };
        psi.Environment["MDREADER_LOG"] = LogPath;

        Process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start mdreader");
    }

    public static string ExePath
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MdReader.slnx")))
            {
                dir = dir.Parent!;
            }

            if (dir is null)
            {
                throw new InvalidOperationException("repo root not found");
            }

#if DEBUG
            const string Config = "Debug";
#else
            const string Config = "Release";
#endif
            return Path.Combine(dir.FullName, "src", "MdReader.App", "bin", Config, "net10.0-windows", "mdreader.exe");
        }
    }

    public string ReadLog() => File.Exists(LogPath) ? SafeRead() : string.Empty;

    private string SafeRead()
    {
        using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Waits until the log contains at least <paramref name="count"/> occurrences of <paramref name="marker"/>.</summary>
    public async Task<bool> WaitForLogAsync(string marker, TimeSpan timeout, int count = 1)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (CountOf(marker) >= count)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    public int CountOf(string marker)
    {
        var log = ReadLog();
        var count = 0;
        var index = 0;
        while ((index = log.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += marker.Length;
        }

        return count;
    }

    public void Dispose()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
        }

        Process.Dispose();
        try
        {
            File.Delete(LogPath);
        }
        catch (IOException)
        {
        }
    }
}
