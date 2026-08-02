using System.Runtime.InteropServices;
using System.Text;

namespace MdReader.App.Services;

/// <summary>
/// Detects whether the app is running as an MSIX package (Microsoft Store or
/// sideloaded). Packaged installs declare file associations in the manifest and
/// update through the Store, so the registry self-registration and the GitHub
/// update check are skipped in that mode.
/// </summary>
public static class PackagedContext
{
    private const int AppModelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);

    private static readonly Lazy<bool> IsPackagedLazy = new(() =>
    {
        try
        {
            var length = 0;
            var result = GetCurrentPackageFullName(ref length, null);
            return result != AppModelErrorNoPackage;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false; // pre-Win8 API absence — definitely unpackaged
        }
    });

    public static bool IsPackaged => IsPackagedLazy.Value;
}
