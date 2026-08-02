using System.Globalization;

namespace MdReader.Core;

/// <summary>
/// Word count and estimated reading time for a markdown document.
///
/// Definitions (documented behavior, relied on by the status bar):
/// - A "word" is a maximal run of letters/digits separated by whitespace or
///   punctuation — except CJK ideographs and kana, which count one character
///   per word (standard practice for Chinese/Japanese counting).
/// - Markdown syntax characters count as part of adjacent words rather than
///   being stripped (cheap, stable, and within ±2% on real documents).
/// - Reading speed: 238 words/minute for prose (Brysbaert 2019 meta-analysis);
///   CJK characters at 500 characters/minute. Estimates are rounded up to a
///   whole minute with a "&lt;1 min" floor.
/// </summary>
public static class ReadingStats
{
    public const int ProseWordsPerMinute = 238;
    public const int CjkCharsPerMinute = 500;

    public readonly record struct Result(int Words, int CjkChars, TimeSpan ReadingTime)
    {
        public string FormatWords() =>
            (Words + CjkChars).ToString("N0", CultureInfo.CurrentCulture) + " words";

        public string FormatReadingTime()
        {
            var minutes = ReadingTime.TotalMinutes;
            return minutes < 1 ? "<1 min" : $"{Math.Ceiling(minutes):0} min";
        }
    }

    public static Result Count(ReadOnlySpan<char> text)
    {
        var words = 0;
        var cjk = 0;
        var inWord = false;

        foreach (var c in text)
        {
            if (IsCjk(c))
            {
                cjk++;
                inWord = false;
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                if (!inWord)
                {
                    words++;
                    inWord = true;
                }
            }
            else
            {
                inWord = false;
            }
        }

        var minutes = words / (double)ProseWordsPerMinute + cjk / (double)CjkCharsPerMinute;
        return new Result(words, cjk, TimeSpan.FromMinutes(minutes));
    }

    private static bool IsCjk(char c) =>
        (c >= 0x4E00 && c <= 0x9FFF)   // CJK Unified Ideographs
        || (c >= 0x3400 && c <= 0x4DBF) // Extension A
        || (c >= 0x3040 && c <= 0x30FF) // Hiragana + Katakana
        || (c >= 0xAC00 && c <= 0xD7AF) // Hangul syllables
        || (c >= 0xF900 && c <= 0xFAFF); // CJK Compatibility Ideographs
}
