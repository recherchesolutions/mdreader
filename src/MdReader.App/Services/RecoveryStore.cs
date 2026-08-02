using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MdReader.Core;

namespace MdReader.App.Services;

public sealed record RecoveryEntry
{
    public int Version { get; init; } = 1;
    public required string OriginalPath { get; init; }
    public required string Text { get; init; }
    public DateTimeOffset SavedAt { get; init; }
}

/// <summary>
/// Crash recovery for unsaved buffers. Snapshots of dirty documents are written
/// (atomically, debounced by the caller) under the per-user app-data folder and
/// offered on the next launch. The original document on disk is never touched —
/// restoring puts the recovered text back into an editor buffer marked dirty.
///
/// Bounds: at most <see cref="MaxEntries"/> snapshots, each at most
/// <see cref="MaxTextBytes"/>; entries older than <see cref="StaleAge"/> are
/// pruned on startup so abandoned data cannot accumulate.
/// </summary>
public sealed class RecoveryStore
{
    public const int MaxEntries = 20;
    public const int MaxTextBytes = 10 * 1024 * 1024;
    public static readonly TimeSpan StaleAge = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly string _root;

    /// <param name="root">Overridable for tests; defaults to %APPDATA%\mdreader\recovery.</param>
    public RecoveryStore(string? root = null)
    {
        _root = root ?? Path.Combine(AppSettings.SettingsDirectory, "recovery");
    }

    private string EntryPath(string originalPath)
    {
        var normalized = Path.GetFullPath(originalPath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24];
        return Path.Combine(_root, hash + ".json");
    }

    /// <summary>Writes/updates the snapshot for a document. Best-effort: recovery must never break editing.</summary>
    public void Save(string originalPath, string text)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(text) > MaxTextBytes)
            {
                return; // oversized buffers are not snapshotted (documented bound)
            }

            Directory.CreateDirectory(_root);

            // Enforce the entry cap: refuse new entries beyond it (existing
            // entries keep updating).
            var entryPath = EntryPath(originalPath);
            if (!File.Exists(entryPath) &&
                Directory.EnumerateFiles(_root, "*.json").Count() >= MaxEntries)
            {
                DiagLog.Write("recovery: entry cap reached, snapshot skipped");
                return;
            }

            var entry = new RecoveryEntry
            {
                OriginalPath = Path.GetFullPath(originalPath),
                Text = text,
                SavedAt = DateTimeOffset.Now,
            };

            AtomicFileWriter.Write(entryPath, JsonSerializer.SerializeToUtf8Bytes(entry, JsonOpts));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DiagLog.Write($"recovery save failed: {ex.Message}");
        }
    }

    /// <summary>Removes the snapshot for a document (after save, or explicit discard).</summary>
    public void Remove(string originalPath)
    {
        try
        {
            File.Delete(EntryPath(originalPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagLog.Write($"recovery remove failed: {ex.Message}");
        }
    }

    /// <summary>
    /// All recoverable snapshots, pruning stale and unreadable ones as a side
    /// effect. Entries whose original file no longer exists are kept (the user
    /// may still want the text) but flagged by the caller via File.Exists.
    /// </summary>
    public IReadOnlyList<RecoveryEntry> ListAndPrune()
    {
        var result = new List<RecoveryEntry>();
        if (!Directory.Exists(_root))
        {
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(_root, "*.json"))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<RecoveryEntry>(File.ReadAllBytes(file));
                if (entry is null || entry.Version != 1 ||
                    DateTimeOffset.Now - entry.SavedAt > StaleAge)
                {
                    File.Delete(file);
                    continue;
                }

                result.Add(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Corrupt snapshot: remove it rather than fail every launch.
                try
                {
                    File.Delete(file);
                }
                catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return result.OrderByDescending(e => e.SavedAt).ToList();
    }
}
