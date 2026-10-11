using System.Text.Json.Serialization;

namespace SmartMixer.Core;

public enum AssignmentMode { Automatic, Fixed, Hybrid }
public enum OrderingMode { Alphabetical, RecentlyOpened, MostRecentlyUsed, PlayingAudio, ForegroundFirst, AudioActivity, CustomPriority }
public enum MixerMode { Applications, Firefox }

public sealed class ApplicationIdentity
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "Available";
    public string ExecutablePath { get; set; } = "";
    public string AppUserModelId { get; set; } = "";
    public int ProcessId { get; set; }
    public bool IsRunning { get; set; }
    public bool IsPlaying { get; set; }
    public bool IsForeground { get; set; }
    public float Volume { get; set; } = 1;
    public bool IsMuted { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime LastUsedUtc { get; set; }
    public int Priority { get; set; }
}

public sealed class ChannelConfiguration
{
    public int Number { get; set; }
    public ApplicationIdentity? Application { get; set; }
    public bool IsFixed { get; set; }
    public bool IsIgnored { get; set; }
    public PotentiometerCalibration Calibration { get; set; } = new();
}

public sealed class PotentiometerCalibration
{
    public int RawMinimum { get; set; }
    public int RawMaximum { get; set; } = 1023;
    public double DeadzonePercent { get; set; } = 2;
    public double SmoothingPercent { get; set; } = 10;
    public double Sensitivity { get; set; } = 1;
    public bool Inverted { get; set; }
    public bool SoftTakeover { get; set; } = true;

    public double Normalize(int raw)
    {
        var minimum = Math.Clamp(RawMinimum, 0, 1022);
        var maximum = Math.Clamp(RawMaximum, minimum + 1, 1023);
        var value = Math.Clamp((raw - minimum) / (double)(maximum - minimum), 0, 1);
        if (Inverted) value = 1 - value;
        var deadzone = Math.Clamp(DeadzonePercent / 100, 0, 0.25);
        value = Math.Clamp((value - deadzone) / (1 - 2 * deadzone), 0, 1);
        return Math.Clamp(Math.Pow(value, 1 / Math.Clamp(Sensitivity, 0.5, 2)), 0, 1);
    }
}

public sealed class DetectionConfiguration
{
    public bool DetectAllAudioApplications { get; set; } = true;
    public bool OnlyActiveApplications { get; set; }
    public bool OnlyPlayingAudio { get; set; }
    public bool IncludeBackgroundApplications { get; set; } = true;
    public bool PrioritizeForeground { get; set; } = true;
    public List<string> IgnoredApplications { get; set; } = [];
}

public sealed class FirefoxConfiguration
{
    public bool Enabled { get; set; } = true;
    public bool TouchTogglesMode { get; set; } = true;
    public int MaximumTabs { get; set; } = 7;
    public OrderingMode Ordering { get; set; } = OrderingMode.PlayingAudio;
    public List<string> IgnoredDomains { get; set; } = [];
    public Dictionary<string, int> TabPriorities { get; set; } = new(StringComparer.Ordinal);
}

public sealed class OverlayConfiguration
{
    public bool Enabled { get; set; } = true;
    public bool ShowApplicationName { get; set; } = true;
    public bool ShowIcon { get; set; } = true;
    public bool ShowPercentage { get; set; } = true;
    public double DurationSeconds { get; set; } = 1.5;
    public string Position { get; set; } = "Bottom";
}

public sealed class MixerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Default";
    public AssignmentMode AssignmentMode { get; set; } = AssignmentMode.Automatic;
    public OrderingMode Ordering { get; set; } = OrderingMode.PlayingAudio;
    public List<ChannelConfiguration> Channels { get; set; } = [];
    public Dictionary<string, int> ApplicationPriorities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DetectionConfiguration Detection { get; set; } = new();
    public FirefoxConfiguration Firefox { get; set; } = new();
    public OverlayConfiguration Overlay { get; set; } = new();

    public MixerProfile Clone(string? name = null)
    {
        var clone = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        var copy = System.Text.Json.JsonSerializer.Deserialize<MixerProfile>(
            System.Text.Json.JsonSerializer.Serialize(this, clone), clone)!;
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = name ?? $"{Name} copy";
        return copy;
    }
}

public sealed class MixerConfiguration
{
    public int SchemaVersion { get; set; } = 1;
    public string ActiveProfileId { get; set; } = "";
    public string DefaultProfileId { get; set; } = "";
    public string? SerialPortName { get; set; }
    public int BaudRate { get; set; } = 115200;
    public string LastMode { get; set; } = nameof(MixerMode.Applications);
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool StartInBackground { get; set; } = true;
    public string Theme { get; set; } = "System";
    public bool AnimationsEnabled { get; set; } = true;
    public bool CompactMode { get; set; }
    public bool EnableProfileAutomation { get; set; }
    public bool SetupCompleted { get; set; }
    public bool SetupCalibrationCompleted { get; set; }
    public List<ProfileAutomationRule> ProfileAutomationRules { get; set; } = [];
    public List<MixerProfile> Profiles { get; set; } = [];

    [JsonIgnore]
    public MixerProfile ActiveProfile => Profiles.First(p => p.Id == ActiveProfileId);

    public static MixerConfiguration CreateDefault()
    {
        var profile = new MixerProfile { Name = "Default" };
        for (var i = 1; i <= 7; i++)
            profile.Channels.Add(new ChannelConfiguration { Number = i });
        return new MixerConfiguration
        {
            ActiveProfileId = profile.Id,
            DefaultProfileId = profile.Id,
            Profiles = [profile]
        };
    }
}

public sealed class ProfileAutomationRule
{
    public string ApplicationKey { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
}

public sealed record PotentiometerSample(int Channel, int RawValue, double Percent);
public sealed record AudioSessionSnapshot(string Key, string Name, string ProcessPath, int ProcessId, double Volume, bool IsMuted, bool IsPlaying);
public sealed record FirefoxTab(
    string Id,
    int NativeTabId,
    string Title,
    string Domain,
    string? FaviconUrl,
    bool IsPlaying,
    bool IsMuted,
    double Volume = 1,
    long OpenedAtUnixMilliseconds = 0,
    long LastActivatedAtUnixMilliseconds = 0,
    bool IsActive = false);
