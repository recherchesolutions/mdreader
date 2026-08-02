using System.IO;
using System.Text.Json;
using MdReader.Core;

namespace MdReader.App.Services;

/// <summary>
/// Persists each file's last reader source line across restarts so reopening a
/// document lands where the user left off. Bounded LRU (200 files), versioned
/// JSON under app data, no document contents stored. Entries for files that no
/// longer exist are pruned on load.
/// </summary>
public sealed class ScrollPositionStore
{
    public const int MaxEntries = 200;

    private sealed record Model(int Version, Dictionary<string, Entry> Entries);

    public sealed record Entry(int Line, DateTimeOffset Touched);

    private readonly string _path;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _sync = new();

    /// <param name="path">Overridable for tests; defaults to app data.</param>
    /// <param name="pruneMissingFiles">Disable in tests that use fake paths.</param>
    public ScrollPositionStore(string? path = null, bool pruneMissingFiles = true)
    {
        _path = path ?? Path.Combine(AppSettings.SettingsDirectory, "scroll-positions.json");
        Load(pruneMissingFiles);
    }

    private void Load(bool pruneMissingFiles)
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var model = JsonSerializer.Deserialize<Model>(File.ReadAllBytes(_path));
            if (model is null || model.Version != 1)
            {
                return; // unknown version: start fresh rather than misread it
            }

            foreach (var (file, entry) in model.Entries)
            {
                if (!pruneMissingFiles || File.Exists(file))
                {
                    _entries[file] = entry;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            DiagLog.Write($"scroll store load failed: {ex.Message}");
        }
    }

    public int? Get(string filePath)
    {
        lock (_sync)
        {
            return _entries.TryGetValue(Path.GetFullPath(filePath), out var entry) ? entry.Line : null;
        }
    }

    public void Set(string filePath, int line)
    {
        lock (_sync)
        {
            _entries[Path.GetFullPath(filePath)] = new Entry(Math.Max(1, line), DateTimeOffset.Now);

            while (_entries.Count > MaxEntries)
            {
                var oldest = _entries.MinBy(kv => kv.Value.Touched);
                _entries.Remove(oldest.Key);
            }
        }
    }

    /// <summary>Persists to disk (atomic). Called on tab close and app exit — not per scroll event.</summary>
    public void Save()
    {
        try
        {
            Dictionary<string, Entry> snapshot;
            lock (_sync)
            {
                snapshot = new Dictionary<string, Entry>(_entries, StringComparer.OrdinalIgnoreCase);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            AtomicFileWriter.Write(_path, JsonSerializer.SerializeToUtf8Bytes(new Model(1, snapshot)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagLog.Write($"scroll store save failed: {ex.Message}");
        }
    }
}
