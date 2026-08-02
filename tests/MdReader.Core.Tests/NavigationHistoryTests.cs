using FluentAssertions;

namespace MdReader.Core.Tests;

public class NavigationHistoryTests
{
    [Fact]
    public void Fresh_history_cannot_navigate()
    {
        var h = new NavigationHistory();
        h.CanGoBack.Should().BeFalse();
        h.CanGoForward.Should().BeFalse();
        h.GoBack(1).Should().BeNull();
        h.GoForward(1).Should().BeNull();
    }

    [Fact]
    public void Back_returns_to_jump_origin_and_forward_returns_to_target()
    {
        var h = new NavigationHistory();
        h.RecordJump(fromLine: 10);   // user jumped 10 → 200

        h.GoBack(currentLine: 200).Should().Be(10);
        h.CanGoForward.Should().BeTrue();
        h.GoForward(currentLine: 10).Should().Be(200);
    }

    [Fact]
    public void New_jump_clears_forward_stack()
    {
        var h = new NavigationHistory();
        h.RecordJump(10);
        h.GoBack(200);
        h.CanGoForward.Should().BeTrue();

        h.RecordJump(10); // jumping somewhere new invalidates forward
        h.CanGoForward.Should().BeFalse();
    }

    [Fact]
    public void Chained_jumps_walk_back_in_order()
    {
        var h = new NavigationHistory();
        h.RecordJump(1);
        h.RecordJump(50);
        h.RecordJump(120);

        h.GoBack(300).Should().Be(120);
        h.GoBack(120).Should().Be(50);
        h.GoBack(50).Should().Be(1);
        h.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void Duplicate_origin_collapses()
    {
        var h = new NavigationHistory();
        h.RecordJump(10);
        h.RecordJump(10);

        h.GoBack(99).Should().Be(10);
        h.CanGoBack.Should().BeFalse("consecutive jumps from the same line are one entry");
    }

    [Fact]
    public void Depth_is_bounded()
    {
        var h = new NavigationHistory();
        for (var i = 1; i <= NavigationHistory.MaxDepth + 50; i++)
        {
            h.RecordJump(i);
        }

        var steps = 0;
        while (h.GoBack(0) is not null)
        {
            steps++;
        }

        steps.Should().Be(NavigationHistory.MaxDepth);
    }
}
