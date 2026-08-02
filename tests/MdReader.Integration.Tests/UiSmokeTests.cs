using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using FluentAssertions;

namespace MdReader.Integration.Tests;

/// <summary>
/// Minimal FlaUI smoke per §7: launch, open a fixture, toggle modes, close.
/// Deeper interactions (typing into Monaco) are intentionally avoided — they
/// depend on desktop focus and are flaky on busy machines.
/// </summary>
// Non-blocking in CI: FlaUI window automation on hosted runners depends on an
// interactive desktop and window focus, which flake independently of app
// correctness (exit criterion: two consecutive weeks green in the non-blocking
// lane, then promote). The deterministic critical path is covered by the
// "blocking" suite (AppHarness log-driven, no desktop interaction).
[Trait("suite", "nonblocking")]
public class UiSmokeTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public async Task Launch_open_toggle_close()
    {
        using var harness = new AppHarness($"\"{FixturePath("basic.md")}\"");

        (await harness.WaitForLogAsync("render complete", TimeSpan.FromSeconds(30)))
            .Should().BeTrue("the document must render");

        using var automation = new UIA3Automation();
        var app = Application.Attach(harness.Process.Id);
        var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));

        window.Should().NotBeNull();
        window!.Title.Should().Contain("basic.md");
        window.Title.Should().Contain("mdreader");

        // Toggle to Source via the status-bar button (click avoids focus issues).
        var modeButton = window.FindFirstDescendant(cf => cf.ByAutomationId("ModeToggleButton"))?.AsButton();
        modeButton.Should().NotBeNull("the status bar mode button must exist");
        modeButton!.Invoke();

        (await harness.WaitForLogAsync("editor ready", TimeSpan.FromSeconds(30)))
            .Should().BeTrue("toggling to Source must initialize Monaco");

        // And back to Reader.
        var sourceButton = window.FindFirstDescendant(cf => cf.ByAutomationId("ModeToggleButton"))?.AsButton();
        sourceButton.Should().NotBeNull();
        sourceButton!.Invoke();

        window.Close();
        harness.Process.WaitForExit(10000).Should().BeTrue("closing the window must exit the app");
    }
}
