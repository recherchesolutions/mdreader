namespace MdReader.Core;

/// <summary>
/// Custom theme discovery. A theme is a single CSS file dropped into
/// %APPDATA%\mdreader\themes\ that overrides the CSS custom properties defined
/// at the top of reader.css. Selecting one appends it after reader.css.
/// </summary>
public static class ThemeLoader
{
    public static string ThemesDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdreader", "themes");

    /// <summary>Names (file names without extension) of available custom themes.</summary>
    public static IReadOnlyList<string> ListCustomThemes()
    {
        if (!Directory.Exists(ThemesDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(ThemesDirectory, "*.css")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Reads a custom theme's CSS, or null if it does not exist or is unreadable.</summary>
    public static string? ReadCustomTheme(string name)
    {
        // The name comes from settings; sanitize it back to a plain file name so a
        // crafted settings file cannot read arbitrary paths.
        var fileName = Path.GetFileName(name) + ".css";
        var path = Path.Combine(ThemesDirectory, fileName);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
