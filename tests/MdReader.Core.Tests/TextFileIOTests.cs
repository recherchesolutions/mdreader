using System.Text;
using FluentAssertions;

namespace MdReader.Core.Tests;

/// <summary>
/// Round-trip tests: open → save with no edits → byte-identical file, across
/// encodings, BOMs, line endings, and trailing-newline states. Silently
/// rewriting any of these corrupts users' Git diffs.
/// </summary>
public class TextFileIOTests
{
    public static TheoryData<string, byte[]> RoundTripCases()
    {
        var utf8 = new UTF8Encoding(false);
        var utf8Bom = new UTF8Encoding(true);
        var utf16Le = new UnicodeEncoding(false, true);
        var utf16Be = new UnicodeEncoding(true, true);
        var utf16LeNoBom = new UnicodeEncoding(false, false);

        return new TheoryData<string, byte[]>
        {
            { "utf8-lf", utf8.GetBytes("# Title\n\nBody text\n") },
            { "utf8-crlf", utf8.GetBytes("# Title\r\n\r\nBody text\r\n") },
            { "utf8-no-trailing-newline", utf8.GetBytes("# Title\r\n\r\nBody text") },
            { "utf8-mixed-endings", utf8.GetBytes("line1\r\nline2\nline3\r\nline4") },
            { "utf8-bom-crlf", Concat(utf8Bom.GetPreamble(), utf8.GetBytes("# Title\r\nBody\r\n")) },
            { "utf8-emoji-cjk", utf8.GetBytes("# 日本語 🎉\n\n中文 한국어\n") },
            { "utf16-le-bom", Concat(utf16Le.GetPreamble(), utf16LeNoBom.GetBytes("# Title\r\nBody\r\n")) },
            { "utf16-be-bom", Concat(utf16Be.GetPreamble(), new UnicodeEncoding(true, false).GetBytes("# Title\r\nBody\r\n")) },
            { "utf16-le-no-bom", utf16LeNoBom.GetBytes("# Title\r\n\r\nA longer body so the heuristic has enough to see.\r\n") },
            { "empty-file", Array.Empty<byte>() },
            { "cr-only", utf8.GetBytes("line1\rline2\rline3") },
            { "latin1-legacy", Encoding.Latin1.GetBytes("café naïve résumé\r\n") },
        };
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void NoEdit_save_is_byte_identical(string name, byte[] original)
    {
        var info = TextFileIO.Decode(original);
        var written = TextFileIO.EncodeForSave(info, info.Text);
        written.Should().Equal(original, $"case '{name}' must round-trip byte-identically");
    }

    [Fact]
    public void Edited_save_preserves_utf8_bom()
    {
        var original = Concat(new UTF8Encoding(true).GetPreamble(), "# Old\r\n"u8.ToArray());
        var info = TextFileIO.Decode(original);

        var written = TextFileIO.EncodeForSave(info, "# New\r\n");

        written.Take(3).Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF });
        new UTF8Encoding(false).GetString(written, 3, written.Length - 3).Should().Be("# New\r\n");
    }

    [Fact]
    public void Edited_save_preserves_utf16_encoding_and_bom()
    {
        var enc = new UnicodeEncoding(false, true);
        var original = Concat(enc.GetPreamble(), new UnicodeEncoding(false, false).GetBytes("old\r\n"));
        var info = TextFileIO.Decode(original);

        var written = TextFileIO.EncodeForSave(info, "new\r\n");

        written.Take(2).Should().Equal(new byte[] { 0xFF, 0xFE });
        new UnicodeEncoding(false, false).GetString(written, 2, written.Length - 2).Should().Be("new\r\n");
    }

    [Fact]
    public void Edited_save_without_bom_stays_without_bom()
    {
        var info = TextFileIO.Decode("plain\n"u8.ToArray());
        var written = TextFileIO.EncodeForSave(info, "edited\n");
        written.Should().Equal("edited\n"u8.ToArray());
    }

    [Theory]
    [InlineData("a\r\nb\r\n", LineEnding.Crlf, "\r\n")]
    [InlineData("a\nb\n", LineEnding.Lf, "\n")]
    [InlineData("a\rb", LineEnding.Cr, "\n")]
    [InlineData("a\r\nb\nc", LineEnding.Mixed, "\r\n")]
    [InlineData("no newlines", LineEnding.None, "\r\n")]
    public void Line_ending_detection(string text, LineEnding expected, string expectedEol)
    {
        var info = TextFileIO.Decode(new UTF8Encoding(false).GetBytes(text));
        info.LineEnding.Should().Be(expected);
        TextFileIO.DominantEol(info).Should().Be(expectedEol);
    }

    [Fact]
    public void File_read_write_round_trip_on_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mdreader-test-{Guid.NewGuid():N}.md");
        var original = Concat(new UTF8Encoding(true).GetPreamble(), "# Disk\r\ntest\r\n"u8.ToArray());
        try
        {
            File.WriteAllBytes(path, original);
            var info = TextFileIO.Read(path);
            TextFileIO.Write(path, info, info.Text);
            File.ReadAllBytes(path).Should().Equal(original);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }
}
