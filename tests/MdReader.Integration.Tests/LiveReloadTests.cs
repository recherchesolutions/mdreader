using FluentAssertions;

namespace MdReader.Integration.Tests;

[Trait("suite", "blocking")]
public class LiveReloadTests : IDisposable
{
    private readonly string _docPath;

    public LiveReloadTests()
    {
        _docPath = Path.Combine(Path.GetTempPath(), $"mdreader-doc-{Guid.NewGuid():N}.md");
        File.WriteAllText(_docPath, "# Live reload test\n\ninitial content\n");
    }

    public void Dispose() => File.Delete(_docPath);

    [Fact]
    public async Task Clean_buffer_reloads_on_external_change_with_debounce()
    {
        using var app = new AppHarness($"\"{_docPath}\"");

        (await app.WaitForLogAsync("render complete", TimeSpan.FromSeconds(30)))
            .Should().BeTrue($"the initial render must happen; log tail: {app.LogTail()}");

        var rendersBefore = app.CountOf("render complete");

        // Simulate an editor writing in bursts: several writes within the
        // 250ms debounce window must produce exactly one reload.
        for (var i = 0; i < 4; i++)
        {
            File.WriteAllText(_docPath, $"# Live reload test\n\nupdated content pass {i}\n");
            await Task.Delay(50);
        }

        (await app.WaitForLogAsync("render complete", TimeSpan.FromSeconds(10), rendersBefore + 1))
            .Should().BeTrue("the external change must trigger a re-render");

        // Allow any (incorrect) extra reloads to surface, then assert exactly one.
        await Task.Delay(1500);
        app.CountOf("render complete").Should().Be(rendersBefore + 1,
            "burst writes within the debounce window must coalesce into one reload");
    }

    [Fact]
    public async Task Version_flag_prints_and_exits()
    {
        using var app = new AppHarness("--version");
        app.Process.WaitForExit(15000).Should().BeTrue("--version must exit promptly");
        app.Process.ExitCode.Should().Be(0);
    }
}
