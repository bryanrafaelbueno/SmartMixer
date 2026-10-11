namespace SmartMixer.Core;

public interface IAssignmentStrategy
{
    string Name { get; }
    IReadOnlyList<ApplicationIdentity> Order(IEnumerable<ApplicationIdentity> applications, OrderingMode ordering);
}

public sealed class AutomaticAssignmentStrategy : IAssignmentStrategy
{
    public string Name => "Automatic";

    public IReadOnlyList<ApplicationIdentity> Order(IEnumerable<ApplicationIdentity> applications, OrderingMode ordering)
    {
        var candidates = applications.Where(a => a.IsRunning);
        ApplicationIdentity[] ordered = ordering switch
        {
            OrderingMode.Alphabetical => candidates.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            OrderingMode.RecentlyOpened => candidates
                .OrderByDescending(a => a.StartedUtc == DateTime.MinValue ? a.LastSeenUtc : a.StartedUtc)
                .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            OrderingMode.MostRecentlyUsed => candidates
                .OrderByDescending(a => a.LastUsedUtc == DateTime.MinValue ? a.LastSeenUtc : a.LastUsedUtc)
                .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            OrderingMode.PlayingAudio => candidates.OrderByDescending(a => a.IsPlaying).ThenByDescending(a => a.IsForeground).ThenByDescending(a => a.LastSeenUtc).ToArray(),
            OrderingMode.ForegroundFirst => candidates.OrderByDescending(a => a.IsForeground).ThenByDescending(a => a.IsPlaying).ThenBy(a => a.DisplayName).ToArray(),
            OrderingMode.AudioActivity => candidates.OrderByDescending(a => a.IsPlaying).ThenBy(a => a.DisplayName).ToArray(),
            OrderingMode.CustomPriority => candidates.OrderBy(a => a.Priority).ThenBy(a => a.DisplayName).ToArray(),
            _ => candidates.ToArray()
        };
        return ordered;
    }
}

public sealed class AssignmentEngine
{
    private readonly IAssignmentStrategy _automatic = new AutomaticAssignmentStrategy();

    public IReadOnlyList<ChannelConfiguration> Assign(MixerProfile profile, IEnumerable<ApplicationIdentity> applications)
    {
        var channels = profile.Channels.OrderBy(c => c.Number).ToList();
        var runningApplications = applications.ToList();
        var fixedChannels = profile.AssignmentMode switch
        {
            AssignmentMode.Fixed => channels.Where(c => c.Application is not null),
            AssignmentMode.Hybrid => channels.Where(c => c.IsFixed && c.Application is not null),
            _ => []
        };
        foreach (var channel in fixedChannels)
        {
            var saved = channel.Application!;
            var current = runningApplications.FirstOrDefault(app => app.IsRunning && (
                string.Equals(app.Key, saved.Key, StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(saved.ExecutablePath)
                    && string.Equals(app.ExecutablePath, saved.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(saved.AppUserModelId)
                    && string.Equals(app.AppUserModelId, saved.AppUserModelId, StringComparison.OrdinalIgnoreCase)));
            if (current is null)
            {
                saved.IsRunning = false;
                saved.IsPlaying = false;
                saved.ProcessId = 0;
            }
            else
            {
                saved.Key = current.Key;
                saved.IsRunning = true;
                saved.IsPlaying = current.IsPlaying;
                saved.IsForeground = current.IsForeground;
                saved.ProcessId = current.ProcessId;
                saved.DisplayName = current.DisplayName;
                saved.ExecutablePath = current.ExecutablePath;
                saved.AppUserModelId = current.AppUserModelId;
                saved.Volume = current.Volume;
                saved.IsMuted = current.IsMuted;
                saved.StartedUtc = current.StartedUtc;
                saved.LastUsedUtc = current.LastUsedUtc;
                saved.LastSeenUtc = current.LastSeenUtc;
            }
        }
        var excluded = profile.Detection.IgnoredApplications
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pool = _automatic.Order(runningApplications.Where(a => a.IsRunning && !excluded.Contains(a.Key)), profile.Ordering)
            .GroupBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(a => profile.Detection.DetectAllAudioApplications || a.IsPlaying)
            .Where(a => !profile.Detection.OnlyPlayingAudio || a.IsPlaying)
            .Where(a => !profile.Detection.OnlyActiveApplications || a.IsForeground || a.IsPlaying)
            .Where(a => profile.Detection.IncludeBackgroundApplications || a.IsForeground || a.IsPlaying)
            .ToList();
        if (profile.Detection.PrioritizeForeground)
            pool = pool.OrderByDescending(a => a.IsForeground).ToList();

        if (profile.AssignmentMode == AssignmentMode.Fixed)
            return channels;

        if (profile.AssignmentMode == AssignmentMode.Automatic)
            foreach (var channel in channels)
                channel.IsFixed = false;

        var fixedKeys = channels.Where(c => profile.AssignmentMode == AssignmentMode.Hybrid && c.IsFixed && c.Application is not null)
            .Select(c => c.Application!.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var available = pool.Where(a => !fixedKeys.Contains(a.Key)).GetEnumerator();

        foreach (var channel in channels)
        {
            if (profile.AssignmentMode == AssignmentMode.Hybrid && channel.IsFixed)
                continue;
            channel.Application = available.MoveNext() ? available.Current : null;
        }

        return channels;
    }
}
