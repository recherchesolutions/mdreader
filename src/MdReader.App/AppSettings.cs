using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MdReader.App;

public enum ThemeChoice
{
    System,
    Light,
    Dark,
}

public enum DefaultMode
{
    Reader,
    Source,
}

public enum LineEndingPolicy
{
    /// <summary>Keep whatever the file uses (default; never corrupt a diff).</summary>
    Preserve,
    Crlf,
    Lf,
}

public enum ExportPreset
{
    Document,
    TechnicalReport,
    Compact,
}

public sealed record SessionTabState(string FilePath, ViewMode Mode);

/// <summary>
/// Application settings, persisted to %APPDATA%\mdreader\settings.json.
/// Telemetry: none. Ever. There is deliberately no field for it.
/// </summary>
public sealed class AppSettings
{
    public DefaultMode DefaultMode { get; set; } = DefaultMode.Reader;
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    /// <summary>Name of a custom CSS theme in %APPDATA%\mdreader\themes, or null.</summary>
    public string? CustomTheme { get; set; }

    public string? FontFamilyOverride { get; set; }
    public int? FontSizeOverride { get; set; }
    public double LineSpacing { get; set; } = 1.65;
    public double ParagraphSpacingEm { get; set; } = 1.0;

    /// <summary>
    /// Reader content column width in px. Null = 720px (~72–78 characters, the
    /// classic reading measure). Defaults to full width (a huge max-width) —
    /// the content column follows the window; narrower measures are opt-in.
    /// </summary>
    public int? ContentWidthOverride { get; set; } = 10000;

    /// <summary>Zoom factor, persisted per app, not per file.</summary>
    public double Zoom { get; set; } = 1.0;

    public bool SplitViewByDefault { get; set; }
    public LineEndingPolicy LineEndingPolicy { get; set; } = LineEndingPolicy.Preserve;

    /// <summary>Load remote (http/https) images globally. Off: tracking vector.</summary>
    public bool LoadRemoteImages { get; set; }

    /// <summary>Extensions (beyond .md/.markdown) the user opted into registering.</summary>
    public List<string> ExtraRegisteredExtensions { get; set; } = [];

    public bool DontAskDefaultApp { get; set; }

    /// <summary>Opt-in check against the GitHub releases API. Default off; no other network use exists.</summary>
    public bool CheckForUpdates { get; set; }

    public List<string> RecentFiles { get; set; } = [];
    public List<string> PinnedFiles { get; set; } = [];
    public bool RestorePreviousSession { get; set; }
    public string AssetDirectoryName { get; set; } = "assets";
    public ExportPreset ExportPreset { get; set; } = ExportPreset.Document;
    public List<SessionTabState> SessionTabs { get; set; } = [];
    public string? ActiveSessionFile { get; set; }
    public bool HasSeenWelcomeDocument { get; set; }

    // Window placement (null = let Windows choose on first run)
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 1100;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }

    public const int MaxRecentFiles = 20;
    public const int MaxPinnedFiles = 20;
    public const int MaxSessionTabs = 50;

    [JsonIgnore]
    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdreader");

    [JsonIgnore]
    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings: fall back to defaults rather than failing startup.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings persistence is best-effort; never crash the app over it.
        }
    }

    public void TouchRecentFile(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles)
        {
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
        }
    }
}
