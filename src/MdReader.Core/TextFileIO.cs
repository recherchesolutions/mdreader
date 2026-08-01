using System.Text;

namespace MdReader.Core;

public enum LineEnding
{
    Crlf,
    Lf,
    Cr,
    Mixed,
    None,
}

/// <summary>
/// Everything needed to write a file back byte-identically: the decoded text plus
/// the encoding, BOM presence, dominant line ending, and trailing-newline state
/// that were detected on read.
/// </summary>
public sealed record TextFileInfo
{
    public required string Text { get; init; }
    public required Encoding Encoding { get; init; }
    public required bool HasBom { get; init; }
    public required LineEnding LineEnding { get; init; }
    public required bool EndsWithNewline { get; init; }
    /// <summary>The exact bytes read from disk, kept so a no-edit save is a byte-identical write.</summary>
    public required byte[] OriginalBytes { get; init; }
}

/// <summary>
/// Reads and writes markdown files while preserving encoding, BOM, and line
/// endings faithfully. Silently rewriting line endings corrupts Git diffs, so a
/// clean (unedited) save writes the original bytes back verbatim, and an edited
/// save re-encodes with the original encoding/BOM and the file's dominant EOL.
/// </summary>
public static class TextFileIO
{
    public static TextFileInfo Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Decode(bytes);
    }

    public static TextFileInfo Decode(byte[] bytes)
    {
        var (encoding, hasBom, bomLength) = DetectEncoding(bytes);
        var text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        var lineEnding = DetectLineEnding(text);

        return new TextFileInfo
        {
            Text = text,
            Encoding = encoding,
            HasBom = hasBom,
            LineEnding = lineEnding,
            EndsWithNewline = text.EndsWith('\n') || text.EndsWith('\r'),
            OriginalBytes = bytes,
        };
    }

    /// <summary>
    /// Encodes <paramref name="newText"/> for saving. When the text is unchanged,
    /// returns the original bytes so the write is byte-identical. Otherwise the
    /// editor's line endings in <paramref name="newText"/> are already normalized
    /// to the file's dominant EOL by the editor configuration; this method just
    /// re-applies encoding and BOM.
    /// </summary>
    public static byte[] EncodeForSave(TextFileInfo original, string newText)
    {
        if (string.Equals(original.Text, newText, StringComparison.Ordinal))
        {
            return original.OriginalBytes;
        }

        var encoding = original.Encoding;
        var body = encoding.GetBytes(newText);
        if (!original.HasBom)
        {
            return body;
        }

        var preamble = encoding.GetPreamble();
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    public static void Write(string path, TextFileInfo original, string newText)
    {
        File.WriteAllBytes(path, EncodeForSave(original, newText));
    }

    /// <summary>The string to configure the editor's EOL with ("\r\n" or "\n").</summary>
    public static string DominantEol(TextFileInfo info) => info.LineEnding switch
    {
        LineEnding.Lf => "\n",
        LineEnding.Cr => "\n", // classic-Mac CR-only is effectively extinct; edits use LF
        _ => "\r\n",           // CRLF, Mixed (dominated by CRLF on Windows), or empty file
    };

    private static (Encoding Encoding, bool HasBom, int BomLength) DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), true, 3);
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: true), true, 4);
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            return (new UTF32Encoding(bigEndian: true, byteOrderMark: true), true, 4);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true), true, 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: true), true, 2);
        }

        // BOM-less UTF-16 heuristic: markdown is overwhelmingly ASCII-dense, so a
        // high ratio of zero bytes in one lane is a strong UTF-16 signal.
        if (bytes.Length >= 4 && LooksLikeUtf16(bytes, out var bigEndian))
        {
            return (new UnicodeEncoding(bigEndian, byteOrderMark: false), false, 0);
        }

        // Try strict UTF-8; fall back to Windows-1252-compatible Latin-1 so that
        // legacy files still open instead of throwing.
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            strictUtf8.GetCharCount(bytes);
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), false, 0);
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.Latin1, false, 0);
        }
    }

    private static bool LooksLikeUtf16(byte[] bytes, out bool bigEndian)
    {
        var sample = Math.Min(bytes.Length, 4096) & ~1;
        int evenZeros = 0, oddZeros = 0;
        for (var i = 0; i < sample; i += 2)
        {
            if (bytes[i] == 0)
            {
                evenZeros++;
            }

            if (bytes[i + 1] == 0)
            {
                oddZeros++;
            }
        }

        var pairs = sample / 2;
        bigEndian = evenZeros > oddZeros;
        return Math.Max(evenZeros, oddZeros) > pairs * 0.4 && Math.Min(evenZeros, oddZeros) < pairs * 0.05;
    }

    private static LineEnding DetectLineEnding(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        var kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
        return kinds switch
        {
            0 => LineEnding.None,
            > 1 => LineEnding.Mixed,
            _ when crlf > 0 => LineEnding.Crlf,
            _ when lf > 0 => LineEnding.Lf,
            _ => LineEnding.Cr,
        };
    }
}
