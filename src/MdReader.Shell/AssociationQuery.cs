using System.Runtime.InteropServices;
using System.Text;

namespace MdReader.Shell;

/// <summary>
/// Asks the shell which executable *effectively* opens an extension right now
/// (AssocQueryString resolves UserChoice, ProgIds, and app defaults the same
/// way Explorer does). Used as the authoritative "are we already the default?"
/// check for the first-run bar, and for the settings page read-out.
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(
    Justification = "Thin AssocQueryString wrapper; result depends on machine-wide shell state")]
public static class AssociationQuery
{
    private const uint AssocFNone = 0;
    private const int AssocStrExecutable = 2;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint AssocQueryStringW(
        uint flags, int str, string pszAssoc, string? pszExtra, StringBuilder? pszOut, ref uint pcchOut);

    /// <summary>Full path of the executable that opens the extension, or null.</summary>
    public static string? GetEffectiveHandlerExecutable(string extension)
    {
        try
        {
            uint length = 0;
            AssocQueryStringW(AssocFNone, AssocStrExecutable, extension, "open", null, ref length);
            if (length == 0)
            {
                return null;
            }

            var sb = new StringBuilder((int)length);
            var hr = AssocQueryStringW(AssocFNone, AssocStrExecutable, extension, "open", sb, ref length);
            return hr == 0 ? sb.ToString() : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>True when the extension effectively opens with the given executable.</summary>
    public static bool OpensWith(string extension, string exePath) =>
        string.Equals(GetEffectiveHandlerExecutable(extension), exePath, StringComparison.OrdinalIgnoreCase);
}
