using SmartMixer.Core;
using Xunit;

namespace SmartMixer.Tests;

public sealed class ConfigurationStoreTests
{
    [Fact]
    public async Task SaveAndLoadRoundTripProfilesAndNormalizesSelection()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "configuration.json");
            var store = new ConfigurationStore(path);
            var configuration = MixerConfiguration.CreateDefault();
            configuration.ActiveProfileId = "missing";
            configuration.DefaultProfileId = "missing";
            configuration.Profiles[0].Firefox.TabPriorities["tab-a"] = 2;

            await store.SaveAsync(configuration);
            var loaded = await store.LoadAsync();

            Assert.Equal(configuration.Profiles[0].Id, loaded.ActiveProfileId);
            Assert.Equal(loaded.ActiveProfileId, loaded.DefaultProfileId);
            Assert.Equal(2, loaded.ActiveProfile.Firefox.TabPriorities["tab-a"]);
            Assert.Equal(Enumerable.Range(1, HardwareConfig.ChannelCount), loaded.ActiveProfile.Channels.Select(channel => channel.Number));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ConcurrentSavesLeaveAValidCompleteConfiguration()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new ConfigurationStore(Path.Combine(directory, "configuration.json"));
            var writes = Enumerable.Range(0, 10).Select(index =>
            {
                var configuration = MixerConfiguration.CreateDefault();
                configuration.Profiles[0].Name = $"Profile {index}";
                configuration.Profiles[0].Channels[0].Application = new ApplicationIdentity
                {
                    Key = $"app-{index}",
                    DisplayName = $"Application {index}"
                };
                return store.SaveAsync(configuration);
            });

            await Task.WhenAll(writes);
            var loaded = await store.LoadAsync();

            Assert.Equal(HardwareConfig.ChannelCount, loaded.ActiveProfile.Channels.Count);
            Assert.StartsWith("Profile ", loaded.ActiveProfile.Name);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task SaveRejectsInvalidCalibrationRange()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new ConfigurationStore(Path.Combine(directory, "configuration.json"));
            var configuration = MixerConfiguration.CreateDefault();
            configuration.ActiveProfile.Channels[0].Calibration.RawMinimum = 500;
            configuration.ActiveProfile.Channels[0].Calibration.RawMaximum = 500;

            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(configuration));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ProfileValidationRejectsInvalidOverlayDuration()
    {
        var profile = MixerConfiguration.CreateDefault().ActiveProfile;
        profile.Overlay.DurationSeconds = double.NaN;

        Assert.Throws<InvalidDataException>(() => ConfigurationStore.ValidateProfile(profile));
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"SmartMixer.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
