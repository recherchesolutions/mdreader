using System.Runtime.CompilerServices;

namespace MdReader.Core.Tests;

public static class TestSupport
{
    /// <summary>
    /// A fixed fake document path so image path rewriting produces deterministic
    /// output for golden files. Rendering never touches the disk for images —
    /// resolution is pure path math — so the file does not need to exist.
    /// </summary>
    public const string FakeDocumentPath = @"C:\docs\project\notes\document.md";

    public static string FixturesDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static string ReadFixture(string relativePath) =>
        File.ReadAllText(Path.Combine(FixturesDirectory, relativePath));

    [ModuleInitializer]
    public static void Init()
    {
        // Snapshots live next to the tests, grouped in one folder.
        Verifier.DerivePathInfo((_, projectDirectory, type, method) =>
            new PathInfo(Path.Combine(projectDirectory, "Snapshots"), type.Name, method.Name));
        // Never pop a diff tool on a failed snapshot (CI and unattended runs).
        DiffEngine.DiffRunner.Disabled = true;
    }
}
