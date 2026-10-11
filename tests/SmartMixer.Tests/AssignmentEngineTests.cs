using SmartMixer.Core;
using Xunit;

namespace SmartMixer.Tests;

public sealed class AssignmentEngineTests
{
    [Fact]
    public void AutomaticAlphabeticalAssignmentUsesSevenIndependentChannels()
    {
        var profile = MixerConfiguration.CreateDefault().ActiveProfile;
        profile.AssignmentMode = AssignmentMode.Automatic;
        profile.Ordering = OrderingMode.Alphabetical;
        var apps = new[] { Create("Zulu"), Create("Alpha"), Create("Mixer") };

        var channels = new AssignmentEngine().Assign(profile, apps);

        Assert.Equal("Alpha", channels[0].Application?.DisplayName);
        Assert.Equal("Mixer", channels[1].Application?.DisplayName);
        Assert.Equal("Zulu", channels[2].Application?.DisplayName);
        Assert.Equal(HardwareConfig.ChannelCount, channels.Count);
    }

    [Fact]
    public void FixedModePreservesTheSelectedApplication()
    {
        var profile = MixerConfiguration.CreateDefault().ActiveProfile;
        profile.AssignmentMode = AssignmentMode.Fixed;
        profile.Channels[0].IsFixed = true;
        profile.Channels[0].Application = Create("Pinned");

        var channels = new AssignmentEngine().Assign(profile, [Create("Other")]);

        Assert.Equal("Pinned", channels[0].Application?.DisplayName);
        Assert.Null(channels[1].Application);
    }

    [Fact]
    public void FixedIdentityRefreshesItsTransientKeyAfterMatchingByExecutablePath()
    {
        var profile = MixerConfiguration.CreateDefault().ActiveProfile;
        profile.AssignmentMode = AssignmentMode.Fixed;
        profile.Channels[0].Application = new ApplicationIdentity
        {
            Key = "legacy-process:42",
            DisplayName = "Pinned",
            ExecutablePath = @"C:\Apps\Pinned.exe"
        };
        var current = new ApplicationIdentity
        {
            Key = @"C:\Apps\Pinned.exe",
            DisplayName = "Pinned (current session)",
            ExecutablePath = @"C:\Apps\Pinned.exe",
            ProcessId = 73,
            IsRunning = true
        };

        new AssignmentEngine().Assign(profile, [current]);

        Assert.Equal(current.Key, profile.Channels[0].Application?.Key);
        Assert.Equal(current.ProcessId, profile.Channels[0].Application?.ProcessId);
        Assert.True(profile.Channels[0].Application?.IsRunning);
    }

    [Fact]
    public void HybridModePreservesFixedChannelsAndRefreshesAutomaticChannels()
    {
        var profile = MixerConfiguration.CreateDefault().ActiveProfile;
        profile.AssignmentMode = AssignmentMode.Hybrid;
        profile.Channels[0].IsFixed = true;
        profile.Channels[0].Application = Create("Pinned");
        profile.Channels[1].Application = Create("Old automatic");

        var channels = new AssignmentEngine().Assign(profile, [Create("New"), Create("Pinned")]);

        Assert.Equal("Pinned", channels[0].Application?.DisplayName);
        Assert.Equal("New", channels[1].Application?.DisplayName);
    }

    [Fact]
    public void AutomaticModeReplacesOldAssignmentsEvenWhenChannelsWereMarkedFixed()
    {
        var profile = MixerConfiguration.CreateDefault().ActiveProfile;
        profile.AssignmentMode = AssignmentMode.Automatic;
        profile.Channels[0].IsFixed = true;
        profile.Channels[0].Application = Create("Old");

        var channels = new AssignmentEngine().Assign(profile, [Create("New")]);

        Assert.Equal("New", channels[0].Application?.DisplayName);
        Assert.False(channels[0].IsFixed);
        Assert.Null(channels[1].Application);
    }

    [Fact]
    public void AutomaticAssignmentDoesNotAssignTheSameApplicationTwice()
    {
        var profile = MixerConfiguration.CreateDefault().ActiveProfile;
        profile.AssignmentMode = AssignmentMode.Automatic;
        var duplicateSession = Create("Discord");
        duplicateSession.Key = "discord";

        var channels = new AssignmentEngine().Assign(profile, [Create("Discord"), duplicateSession]);

        Assert.Equal(1, channels.Count(channel => channel.Application is not null));
    }

    [Fact]
    public void RecentlyOpenedAndMostRecentlyUsedUseDifferentActivityTimestamps()
    {
        var profile = MixerConfiguration.CreateDefault().ActiveProfile;
        var early = Create("Early");
        early.StartedUtc = DateTime.UnixEpoch;
        early.LastUsedUtc = DateTime.UnixEpoch.AddHours(2);
        var late = Create("Late");
        late.StartedUtc = DateTime.UnixEpoch.AddHours(1);
        late.LastUsedUtc = DateTime.UnixEpoch.AddHours(1);

        profile.AssignmentMode = AssignmentMode.Automatic;
        profile.Ordering = OrderingMode.RecentlyOpened;
        var recentlyOpened = new AssignmentEngine().Assign(profile, [early, late]);
        Assert.Equal("Late", recentlyOpened[0].Application?.DisplayName);

        profile.Ordering = OrderingMode.MostRecentlyUsed;
        var mostRecentlyUsed = new AssignmentEngine().Assign(profile, [early, late]);
        Assert.Equal("Early", mostRecentlyUsed[0].Application?.DisplayName);
    }

    private static ApplicationIdentity Create(string name) => new()
    {
        Key = name,
        DisplayName = name,
        IsRunning = true
    };
}
