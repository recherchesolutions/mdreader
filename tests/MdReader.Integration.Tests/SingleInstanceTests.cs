using FluentAssertions;

namespace MdReader.Integration.Tests;

public class SingleInstanceTests : IDisposable
{
    private readonly string _docA;
    private readonly string _docB;

    public SingleInstanceTests()
    {
        _docA = Path.Combine(Path.GetTempPath(), $"mdreader-si-a-{Guid.NewGuid():N}.md");
        _docB = Path.Combine(Path.GetTempPath(), $"mdreader-si-b-{Guid.NewGuid():N}.md");
        File.WriteAllText(_docA, "# Doc A\n");
        File.WriteAllText(_docB, "# Doc B\n");
    }

    public void Dispose()
    {
        File.Delete(_docA);
        File.Delete(_docB);
    }

    [Fact]
    public async Task Second_launch_hands_file_to_running_instance_and_exits()
    {
        var instanceId = Guid.NewGuid().ToString("N");
        using var first = new AppHarness($"\"{_docA}\"", instanceId);
        (await first.WaitForLogAsync("render complete", TimeSpan.FromSeconds(30)))
            .Should().BeTrue("the first instance must be up before the handoff");

        using var second = new AppHarness($"\"{_docB}\"", instanceId);

        // The second process must exit promptly after handing off.
        second.Process.WaitForExit(15000).Should().BeTrue("the second instance must exit after handoff");

        // The first instance must receive the activation and open a second tab.
        (await first.WaitForLogAsync("activation received", TimeSpan.FromSeconds(10)))
            .Should().BeTrue("the running instance must receive the activation");
        (await first.WaitForLogAsync("render complete", TimeSpan.FromSeconds(20), count: 2))
            .Should().BeTrue("the handed-off document must render in the first instance");

        first.Process.HasExited.Should().BeFalse("the owner must keep running");
    }
}
