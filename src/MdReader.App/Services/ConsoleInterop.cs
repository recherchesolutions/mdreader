using System.IO;
using System.Runtime.InteropServices;

namespace MdReader.App.Services;

/// <summary>
/// A WinExe app has no console; attach to the parent's (cmd/PowerShell) so
/// --version and headless export messages are visible when run from a shell.
/// </summary>
public static class ConsoleInterop
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    public static bool TryWriteLine(string text)
    {
        if (!AttachConsole(AttachParentProcess))
        {
            return false;
        }

        try
        {
            // Re-open stdout after attaching.
            using var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            writer.WriteLine();
            writer.WriteLine(text);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
