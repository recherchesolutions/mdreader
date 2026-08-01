using System.IO;
using Microsoft.Win32;

namespace MdReader.App.Services;

/// <summary>Follows the Windows app theme (light/dark) with change notification.</summary>
public static class WindowsTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>Resolves the effective theme name ("light"/"dark") for a setting.</summary>
    public static string Resolve(ThemeChoice choice) => choice switch
    {
        ThemeChoice.Light => "light",
        ThemeChoice.Dark => "dark",
        _ => IsSystemDark() ? "dark" : "light",
    };

    /// <summary>Fires when the user changes the Windows theme. Handlers run on the UI thread.</summary>
    public static event EventHandler? SystemThemeChanged;

    public static void StartListening()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                SystemThemeChanged?.Invoke(null, EventArgs.Empty);
            }
        };
    }
}
