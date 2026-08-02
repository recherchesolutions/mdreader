using FluentAssertions;

namespace MdReader.Core.Tests;

/// <summary>Fault-oriented tests: atomic saves must never corrupt or truncate.</summary>
public sealed class AtomicFileWriterTests : IDisposable
{
    private readonly string _dir;

    public AtomicFileWriterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"mdreader-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string FilePath(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Creates_new_file()
    {
        var path = FilePath("new.md");
        AtomicFileWriter.Write(path, "hello"u8.ToArray());
        File.ReadAllText(path).Should().Be("hello");
    }

    [Fact]
    public void Replaces_existing_file_and_leaves_no_temp_files()
    {
        var path = FilePath("doc.md");
        File.WriteAllText(path, "old");

        AtomicFileWriter.Write(path, "new content"u8.ToArray());

        File.ReadAllText(path).Should().Be("new content");
        Directory.EnumerateFiles(_dir, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Locked_target_surfaces_error_original_intact_temp_cleaned()
    {
        var path = FilePath("locked.md");
        File.WriteAllText(path, "original");

        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var act = () => AtomicFileWriter.Write(path, "replacement"u8.ToArray());
            act.Should().Throw<IOException>().WithMessage("*locked.md*", "the error must name the file");
        }

        File.ReadAllText(path).Should().Be("original", "a failed save must not touch the document");
        Directory.EnumerateFiles(_dir, "*.tmp").Should().BeEmpty("temp files must be cleaned on failure");
    }

    [Fact]
    public void Byte_fidelity_round_trip_through_textfileio()
    {
        // The full save path (TextFileIO.Write → AtomicFileWriter) must keep
        // the no-edit round trip byte-identical.
        var path = FilePath("roundtrip.md");
        var original = new byte[] { 0xEF, 0xBB, 0xBF }.Concat("# Title\r\nBody\r\n"u8.ToArray()).ToArray();
        File.WriteAllBytes(path, original);

        var info = TextFileIO.Read(path);
        TextFileIO.Write(path, info, info.Text);

        File.ReadAllBytes(path).Should().Equal(original);
    }

    [Fact]
    public void Missing_directory_throws_and_creates_nothing()
    {
        var path = Path.Combine(_dir, "nope", "x.md");
        var act = () => AtomicFileWriter.Write(path, "x"u8.ToArray());
        act.Should().Throw<IOException>();
        Directory.Exists(Path.Combine(_dir, "nope")).Should().BeFalse();
    }
}
