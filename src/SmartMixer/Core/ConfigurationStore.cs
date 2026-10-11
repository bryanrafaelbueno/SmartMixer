using System.Text.Json;

namespace SmartMixer.Core;

public sealed class ConfigurationStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public ConfigurationStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartMixer", "configuration.json");
    }

    public static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<MixerConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            var fresh = MixerConfiguration.CreateDefault();
            await SaveAsync(fresh, cancellationToken);
            return fresh;
        }

        await using var stream = File.OpenRead(_path);
        var configuration = await JsonSerializer.DeserializeAsync<MixerConfiguration>(stream, JsonOptions(), cancellationToken)
            ?? throw new InvalidDataException("The SmartMixer configuration file contains no data.");
        Validate(configuration);
        EnsureValidSelection(configuration);
        return configuration;
    }

    public async Task SaveAsync(MixerConfiguration configuration, CancellationToken cancellationToken = default)
    {
        Validate(configuration);
        EnsureValidSelection(configuration);
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("The configuration file path must include a directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            await using (var stream = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions(), cancellationToken);
            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task ExportAsync(MixerConfiguration configuration, string path, CancellationToken cancellationToken = default)
    {
        Validate(configuration);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions(), cancellationToken);
    }

    public async Task<MixerConfiguration> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var configuration = await JsonSerializer.DeserializeAsync<MixerConfiguration>(stream, JsonOptions(), cancellationToken)
            ?? throw new InvalidDataException("The selected file is not a SmartMixer configuration.");
        Validate(configuration);
        EnsureValidSelection(configuration);
        return configuration;
    }

    public static void ValidateProfile(MixerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Validate(new MixerConfiguration
        {
            Profiles = [profile],
            ProfileAutomationRules = []
        });
    }

    private static void Validate(MixerConfiguration configuration)
    {
        if (configuration.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported configuration schema version: {configuration.SchemaVersion}.");
        if (configuration.BaudRate is < 1200 or > 921600)
            throw new InvalidDataException("The configured serial baud rate is outside the supported range.");
        if (configuration.Profiles is null || configuration.Profiles.Count == 0
            || configuration.ProfileAutomationRules is null)
            throw new InvalidDataException("A configuration must contain at least one profile.");
        if (configuration.Profiles.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Name)
            || p.Channels is null || p.Channels.Select(c => c?.Number).Distinct().Count() != HardwareConfig.ChannelCount
            || p.Channels.Count != HardwareConfig.ChannelCount
            || p.Channels.Any(c => c is null || c.Number is < 1 or > HardwareConfig.ChannelCount || c.Calibration is null)
            || !Enum.IsDefined(p.AssignmentMode) || !Enum.IsDefined(p.Ordering)
            || p.ApplicationPriorities is null || p.ApplicationPriorities.Values.Any(priority => priority is < 0 or > 999)
            || p.Detection is null || p.Detection.IgnoredApplications is null
            || p.Firefox is null || p.Firefox.TabPriorities is null
            || p.Firefox.TabPriorities.Values.Any(priority => priority is < 0 or > 999)
            || p.Firefox.IgnoredDomains is null
            || p.Firefox.MaximumTabs is < 1 or > HardwareConfig.ChannelCount || !Enum.IsDefined(p.Firefox.Ordering)
            || p.Overlay is null || !double.IsFinite(p.Overlay.DurationSeconds)
            || p.Overlay.DurationSeconds is < 0.5 or > 5
            || p.Overlay.Position is not ("Center" or "Top" or "Bottom")))
            throw new InvalidDataException("Each profile must contain exactly one configuration for channels 1 through 7.");
        if (configuration.Profiles.Select(profile => profile.Id).Distinct(StringComparer.Ordinal).Count() != configuration.Profiles.Count)
            throw new InvalidDataException("Profile identifiers must be unique.");
        if (configuration.Profiles.Any(profile => profile.Channels.Any(channel =>
                channel.Calibration.RawMinimum is < 0 or > 1022
                || channel.Calibration.RawMaximum is < 1 or > 1023
                || channel.Calibration.RawMinimum >= channel.Calibration.RawMaximum
                || !double.IsFinite(channel.Calibration.DeadzonePercent)
                || channel.Calibration.DeadzonePercent is < 0 or > 10
                || !double.IsFinite(channel.Calibration.SmoothingPercent)
                || channel.Calibration.SmoothingPercent is < 0 or > 50
                || !double.IsFinite(channel.Calibration.Sensitivity)
                || channel.Calibration.Sensitivity is < 0.5 or > 2)))
            throw new InvalidDataException("Potentiometer calibration limits or response settings are outside the supported range.");
    }

    private static void EnsureValidSelection(MixerConfiguration configuration)
    {
        if (!configuration.Profiles.Any(p => p.Id == configuration.ActiveProfileId))
            configuration.ActiveProfileId = configuration.Profiles[0].Id;
        if (!configuration.Profiles.Any(p => p.Id == configuration.DefaultProfileId))
            configuration.DefaultProfileId = configuration.ActiveProfileId;
    }
}
