using SmartMixer.Core;
using Xunit;

namespace SmartMixer.Tests;

public sealed class FirefoxTabAssignmentTests
{
    [Fact]
    public void OrdersTabsByTheirDistinctOpenedAndActivatedTimes()
    {
        var tabs = new[]
        {
            Create("earlier", "Earlier", openedAt: 1, activatedAt: 3),
            Create("later", "Later", openedAt: 2, activatedAt: 2)
        };

        Assert.Equal("later", FirefoxTabAssignment.Order(tabs, OrderingMode.RecentlyOpened)[0].Id);
        Assert.Equal("earlier", FirefoxTabAssignment.Order(tabs, OrderingMode.MostRecentlyUsed)[0].Id);
    }

    [Fact]
    public void ForegroundOrderingPrioritizesTheActiveTab()
    {
        var tabs = new[]
        {
            Create("playing", "Playing", playing: true),
            Create("active", "Active", active: true)
        };

        Assert.Equal("active", FirefoxTabAssignment.Order(tabs, OrderingMode.ForegroundFirst)[0].Id);
    }

    [Fact]
    public void CustomPriorityUsesConfiguredRanksAndAlphabeticalFallback()
    {
        var tabs = new[]
        {
            Create("unranked-z", "Zulu"),
            Create("ranked", "Echo"),
            Create("unranked-a", "Alpha")
        };
        var priorities = new Dictionary<string, int> { ["ranked"] = 1 };

        var ordered = FirefoxTabAssignment.Order(tabs, OrderingMode.CustomPriority, priorities);

        Assert.Equal(new[] { "ranked", "unranked-a", "unranked-z" }, ordered.Select(tab => tab.Id));
    }

    private static FirefoxTab Create(
        string id,
        string title,
        bool playing = false,
        bool active = false,
        long openedAt = 0,
        long activatedAt = 0) =>
        new(id, 1, title, "example.com", null, playing, false, 1, openedAt, activatedAt, active);
}
