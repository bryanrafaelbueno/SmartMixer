using SmartMixer.Core;

namespace SmartMixer.Core;

public static class FirefoxTabAssignment
{
    public static IReadOnlyList<FirefoxTab> Order(
        IEnumerable<FirefoxTab> tabs,
        OrderingMode ordering,
        IReadOnlyDictionary<string, int>? priorities = null) =>
        ordering switch
        {
            OrderingMode.Alphabetical => tabs.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            OrderingMode.AudioActivity or OrderingMode.PlayingAudio => tabs.OrderByDescending(t => t.IsPlaying).ThenBy(t => t.Title).ToArray(),
            OrderingMode.RecentlyOpened => tabs.OrderByDescending(t => t.OpenedAtUnixMilliseconds).ThenBy(t => t.Title).ToArray(),
            OrderingMode.MostRecentlyUsed => tabs.OrderByDescending(t => t.LastActivatedAtUnixMilliseconds).ThenBy(t => t.Title).ToArray(),
            OrderingMode.ForegroundFirst => tabs.OrderByDescending(t => t.IsActive).ThenByDescending(t => t.IsPlaying).ThenBy(t => t.Title).ToArray(),
            OrderingMode.CustomPriority => tabs.OrderBy(t => priorities?.GetValueOrDefault(t.Id, int.MaxValue) ?? int.MaxValue)
                .ThenBy(t => t.Title).ToArray(),
            _ => tabs.OrderByDescending(t => t.IsPlaying).ThenByDescending(t => t.IsActive).ThenBy(t => t.Title).ToArray()
        };
}
