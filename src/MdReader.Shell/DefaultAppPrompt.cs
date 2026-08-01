using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MdReader.Shell;

/// <summary>
/// The only sanctioned paths to becoming the default markdown app: the user
/// makes the choice through standard Windows UI.
///
/// Primary: SHOpenWithDialog with OAIF_FORCE_OPEN_WITH | OAIF_EXEC against a
/// real document — Windows shows "How do you want to open this file?" with the
/// "Always use this app" checkbox, and records the choice correctly.
///
/// Fallback: ms-settings:defaultapps deep-linked to mdreader's registration.
///
/// Note: IApplicationAssociationRegistrationUI::LaunchAdvancedAssociationUI is
/// deprecated on Windows 10+ and deliberately not used here.
/// </summary>
public static class DefaultAppPrompt
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENASINFO
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pcszFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pcszClass;
        public int oaifInFlags;
    }

    private const int OAIF_ALLOW_REGISTRATION = 0x00000001;
    private const int OAIF_EXEC = 0x00000004;
    private const int OAIF_FORCE_OPEN_WITH = 0x00000008;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHOpenWithDialog(IntPtr hwndParent, ref OPENASINFO info);

    /// <summary>
    /// Shows the standard "How do you want to open this file?" dialog for the
    /// given document. Returns true when the dialog was shown successfully.
    /// </summary>
    public static bool ShowOpenWithDialog(IntPtr ownerHwnd, string documentPath)
    {
        var info = new OPENASINFO
        {
            pcszFile = documentPath,
            pcszClass = null,
            oaifInFlags = OAIF_ALLOW_REGISTRATION | OAIF_FORCE_OPEN_WITH | OAIF_EXEC,
        };

        try
        {
            return SHOpenWithDialog(ownerHwnd, ref info) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Opens Windows Settings → Default apps at mdreader's registration.
    /// Returns true when the settings page could be launched.
    /// </summary>
    public static bool OpenDefaultAppsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=mdreader")
            {
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Text-only instructions for the fallback path (no screenshots, per spec).</summary>
    public const string SettingsInstructions =
        "In the Settings page that opened, select \"mdreader\", then choose it for .md and .markdown.";
}
