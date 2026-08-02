using FluentAssertions;
using MdReader.Core;

namespace MdReader.Core.Tests;

public sealed class MarkdownDiagnosticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mdreader-diagnostics-{Guid.NewGuid():N}");

    public MarkdownDiagnosticsTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Reports_missing_local_assets_and_anchors_but_not_remote_urls()
    {
        var path = Path.Combine(_root, "doc.md");
        var markdown = "# Existing\n\n![alt](missing.png)\n[doc](missing.md)\n[bad](#nope)\n[web](https://example.com)";

        var diagnostics = MarkdownDiagnostics.Analyze(markdown, path);

        diagnostics.Select(d => d.Code).Should().BeEquivalentTo(["MD001", "MD004", "MD002"]);
        diagnostics.Should().OnlyContain(d => d.Line > 0);
    }

    [Fact]
    public void Existing_local_target_has_no_diagnostic()
    {
        var path = Path.Combine(_root, "doc.md");
        File.WriteAllText(Path.Combine(_root, "other.md"), "# Other");

        MarkdownDiagnostics.Analyze("[other](other.md)", path).Should().BeEmpty();
    }

    [Fact]
    public void Malformed_percent_encoding_is_a_diagnostic_not_an_exception()
    {
        var path = Path.Combine(_root, "doc.md");

        var action = () => MarkdownDiagnostics.Analyze("[bad](broken%ZZ.md)", path);

        action.Should().NotThrow();
        action().Should().ContainSingle(d => d.Code == "MD004" || d.Code == "MD005");
    }
}
