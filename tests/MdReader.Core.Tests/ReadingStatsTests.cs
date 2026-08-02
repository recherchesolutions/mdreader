using FluentAssertions;

namespace MdReader.Core.Tests;

public class ReadingStatsTests
{
    [Fact]
    public void Markdown_count_excludes_code_front_matter_and_link_targets()
    {
        const string text = """
            ---
            secret: hidden metadata words
            ---
            # Visible heading

            Read the [friendly guide](https://example.com/hidden/target/words).

            `inline hidden code`

            ```csharp
            many hidden code words here
            ```
            """;

        var result = ReadingStats.CountMarkdown(text);

        result.Words.Should().Be(6);
    }

    [Fact]
    public void Empty_document_is_zero_and_under_a_minute()
    {
        var r = ReadingStats.Count("");
        r.Words.Should().Be(0);
        r.CjkChars.Should().Be(0);
        r.FormatReadingTime().Should().Be("<1 min");
    }

    [Theory]
    [InlineData("hello world", 2)]
    [InlineData("one, two; three.", 3)]
    [InlineData("**bold** and _italic_ text", 4)]
    [InlineData("line1\nline2\r\nline3", 3)]
    [InlineData("var x = 42;", 3)]
    [InlineData("  leading and trailing  ", 3)]
    public void Word_counting(string text, int expected)
    {
        ReadingStats.Count(text).Words.Should().Be(expected);
    }

    [Fact]
    public void Cjk_counts_per_character()
    {
        var r = ReadingStats.Count("日本語のテキスト");
        r.CjkChars.Should().Be(8);
        r.Words.Should().Be(0);
    }

    [Fact]
    public void Mixed_latin_and_cjk()
    {
        var r = ReadingStats.Count("The word 漢字 means kanji");
        r.Words.Should().Be(4);
        r.CjkChars.Should().Be(2);
        r.FormatWords().Should().Be("6 words");
    }

    [Fact]
    public void Reading_time_uses_238_wpm_and_rounds_up()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 476)); // exactly 2 minutes
        ReadingStats.Count(text).FormatReadingTime().Should().Be("2 min");

        var justOver = string.Join(' ', Enumerable.Repeat("word", 480));
        ReadingStats.Count(justOver).FormatReadingTime().Should().Be("3 min");
    }

    [Fact]
    public void Large_document_is_fast_and_sane()
    {
        var text = string.Join(' ', Enumerable.Repeat("lorem ipsum dolor", 200_000));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = ReadingStats.Count(text);
        sw.Stop();

        r.Words.Should().Be(600_000);
        sw.ElapsedMilliseconds.Should().BeLessThan(500, "counting is a single linear scan");
    }
}
