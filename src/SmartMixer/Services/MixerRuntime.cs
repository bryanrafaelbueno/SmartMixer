using SmartMixer.Core;
using ApplicationIdentity = SmartMixer.Core.ApplicationIdentity;

namespace SmartMixer.Services;

public sealed class MixerRuntime
{
    private readonly bool[] _pickupAcquired = new bool[HardwareConfig.ChannelCount];
    private readonly double[] _smoothedValues = new double[HardwareConfig.ChannelCount];
    private readonly bool[] _smoothingPrimed = new bool[HardwareConfig.ChannelCount];
    private readonly double[] _lastAppliedValues = new double[HardwareConfig.ChannelCount];
    private readonly bool[] _hasLastAppliedValue = new bool[HardwareConfig.ChannelCount];
    private readonly CancellationTokenSource _backgroundStop = new();
    private readonly SemaphoreSlim _reassignLock = new(1, 1);
    private readonly ForegroundDetector _foregroundDetector = new();
    private readonly Dictionary<string, DateTime> _lastUsed = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastReconnectError;
    private Task? _monitorTask;
    private bool _stopped;
    private bool _applyingProfileAutomation;

    public ConfigurationStore ConfigurationStore { get; } = new();
    public StructuredLogger Logger { get; } = new();
    public ArduinoConnectionManager Arduino { get; } = new();
    public AudioSessionManager Audio { get; } = new();
    public FirefoxConnector Firefox { get; } = new();
    public AssignmentEngine Assignment { get; } = new();
    public MixerConfiguration Configuration { get; private set; } = MixerConfiguration.CreateDefault();
    public MixerMode Mode { get; private set; } = MixerMode.Applications;
    public event EventHandler<AudioSessionSnapshot>? VolumeAdjusted;
    public event EventHandler? AssignmentsChanged;
    public event Action<MixerMode>? ModeChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Configuration = await ConfigurationStore.LoadAsync(cancellationToken);
        Mode = Enum.TryParse<MixerMode>(Configuration.LastMode, out var savedMode)
            && (savedMode != MixerMode.Firefox || Configuration.ActiveProfile.Firefox.Enabled)
                ? savedMode
                : MixerMode.Applications;
        Configuration.LastMode = Mode.ToString();
        Audio.SessionsChanged += Audio_SessionsChanged;
        Firefox.StateChanged += Firefox_StateChanged;
        Firefox.TabsChanged += Firefox_TabsChanged;
        Arduino.StateChanged += Arduino_StateChanged;
        Arduino.TouchDetected += Arduino_TouchDetected;
        Arduino.PotentiometerChanged += Arduino_PotentiometerChanged;

        Audio.Start();
        try
        {
            await Audio.RefreshAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Warning($"Initial Windows audio enumeration failed: {ex.Message}");
        }
        Firefox.Start();
        _monitorTask = MonitorArduinoConnectionAsync(_backgroundStop.Token);
        if (!string.IsNullOrWhiteSpace(Configuration.SerialPortName))
        {
            try { Arduino.Connect(Configuration.SerialPortName, Configuration.BaudRate); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
            {
                Logger.Warning($"Could not reconnect to {Configuration.SerialPortName}: {ex.Message}");
            }
        }
        await ReassignAsync();
        Logger.Info("SmartMixer background runtime started.");
    }

    public async Task StopAsync()
    {
        if (_stopped) return;
        _stopped = true;
        Logger.Info("Stopping SmartMixer background runtime.");
        await _backgroundStop.CancelAsync();
        if (_monitorTask is not null)
            await _monitorTask;
        Logger.Info("SmartMixer Arduino monitor stopped.");
        var arduinoShutdown = Task.Run(Arduino.Dispose);
        try
        {
            await arduinoShutdown.WaitAsync(TimeSpan.FromSeconds(3));
            Logger.Info("SmartMixer Arduino connection stopped.");
        }
        catch (TimeoutException)
        {
            Logger.Warning("Arduino serial shutdown is still pending; continuing application exit.");
            _ = arduinoShutdown.ContinueWith(
                task => Logger.Warning($"Arduino serial shutdown failed: {task.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Logger.Warning($"Arduino serial shutdown failed: {ex.Message}");
        }
        if (await Audio.DisposeAsync())
            Logger.Info("SmartMixer Windows audio sessions stopped.");
        else
            Logger.Warning(Audio.LastError ?? "Windows audio session cleanup was deferred until enumeration completes.");
        await Firefox.DisposeAsync();
        Logger.Info("SmartMixer Firefox connection stopped.");
        await ConfigurationStore.SaveAsync(Configuration);
        Logger.Info("SmartMixer configuration saved.");
        Logger.Info("SmartMixer background runtime stopped.");
        _backgroundStop.Dispose();
    }

    public async Task SetActiveProfileAsync(string profileId)
    {
        if (!Configuration.Profiles.Any(p => p.Id == profileId))
            throw new ArgumentException("The selected profile does not exist.", nameof(profileId));
        Configuration.ActiveProfileId = profileId;
        if (Mode == MixerMode.Firefox && !Configuration.ActiveProfile.Firefox.Enabled)
        {
            Mode = MixerMode.Applications;
            Configuration.LastMode = Mode.ToString();
        }
        Array.Clear(_pickupAcquired);
        Array.Clear(_smoothingPrimed);
        Array.Clear(_hasLastAppliedValue);
        await ConfigurationStore.SaveAsync(Configuration);
        Logger.Info($"Switched to profile: {Configuration.ActiveProfile.Name}.");
        await ReassignAsync();
        UpdateLedState();
    }

    public async Task PersistAsync() => await ConfigurationStore.SaveAsync(Configuration);

    public async Task ImportConfigurationAsync(MixerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (_stopped) throw new InvalidOperationException("The SmartMixer runtime has been stopped.");

        var nextMode = Enum.TryParse<MixerMode>(configuration.LastMode, out var savedMode)
            && (savedMode != MixerMode.Firefox || configuration.ActiveProfile.Firefox.Enabled)
                ? savedMode
                : MixerMode.Applications;
        configuration.LastMode = nextMode.ToString();
        await ConfigurationStore.SaveAsync(configuration);
        Configuration = configuration;
        Mode = nextMode;
        Array.Clear(_pickupAcquired);
        Array.Clear(_smoothingPrimed);
        Array.Clear(_hasLastAppliedValue);

        if (Arduino.IsConnected
            && !string.Equals(Arduino.PortName, configuration.SerialPortName, StringComparison.OrdinalIgnoreCase))
            Arduino.Disconnect();
        if (!Arduino.IsConnected && !string.IsNullOrWhiteSpace(configuration.SerialPortName))
        {
            try { Arduino.Connect(configuration.SerialPortName, configuration.BaudRate); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
            {
                Logger.Warning($"Imported Arduino port {configuration.SerialPortName} could not be opened: {ex.Message}");
            }
        }
        try { await new StartupService().SetEnabledAsync(configuration.StartWithWindows); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException or IOException)
        {
            Logger.Warning($"Imported startup setting could not be applied: {ex.Message}");
        }
        await ReassignAsync();
        UpdateLedState();
    }

    public async Task ReassignAsync()
    {
        await _reassignLock.WaitAsync();
        try
        {
            await ReassignCoreAsync();
        }
        finally
        {
            _reassignLock.Release();
        }
    }

    private async Task ReassignCoreAsync()
    {
        var channels = Configuration.ActiveProfile.Channels.OrderBy(c => c.Number).ToArray();
        var before = channels.Select(c => c.Application?.Key).ToArray();
        if (Mode == MixerMode.Firefox)
        {
            var tabs = FirefoxTabAssignment.Order(
                    Firefox.Tabs.Where(t => !Configuration.ActiveProfile.Firefox.IgnoredDomains.Contains(t.Domain, StringComparer.OrdinalIgnoreCase)),
                    Configuration.ActiveProfile.Firefox.Ordering,
                    Configuration.ActiveProfile.Firefox.TabPriorities)
                .Take(Configuration.ActiveProfile.Firefox.MaximumTabs)
                .ToArray();
            foreach (var channel in channels)
            {
                if (channel.IsFixed)
                {
                    if (channel.Application is { } fixedApplication
                        && fixedApplication.Key.StartsWith("firefox-tab:", StringComparison.Ordinal))
                    {
                        var stableId = fixedApplication.Key["firefox-tab:".Length..];
                        var fixedTab = Firefox.Tabs.FirstOrDefault(tab => tab.Id == stableId);
                        fixedApplication.IsRunning = fixedTab is not null;
                        fixedApplication.IsPlaying = fixedTab?.IsPlaying ?? false;
                        fixedApplication.ProcessId = fixedTab?.NativeTabId ?? 0;
                        if (fixedTab is not null)
                        {
                            fixedApplication.DisplayName = fixedTab.Title;
                            fixedApplication.Volume = (float)fixedTab.Volume;
                            fixedApplication.IsMuted = fixedTab.IsMuted;
                        }
                    }
                    continue;
                }
                var index = channel.Number - 1;
                channel.Application = index < tabs.Length
                    ? new ApplicationIdentity
                    {
                        Key = $"firefox-tab:{tabs[index].Id}",
                        DisplayName = tabs[index].Title,
                        IsRunning = true,
                        IsPlaying = tabs[index].IsPlaying,
                        IsMuted = tabs[index].IsMuted,
                        Volume = (float)tabs[index].Volume,
                        LastSeenUtc = tabs[index].OpenedAtUnixMilliseconds > 0
                            ? DateTimeOffset.FromUnixTimeMilliseconds(tabs[index].OpenedAtUnixMilliseconds).UtcDateTime
                            : DateTime.UtcNow
                    }
                    : null;
            }
        }
        else
        {
            var foregroundProcessId = _foregroundDetector.GetForegroundProcessId();
            var audioCandidates = Audio.Sessions.Select(s =>
            {
                var isForeground = foregroundProcessId == s.ProcessId;
                if (isForeground) _lastUsed[s.Key] = DateTime.UtcNow;
                var processStarted = _foregroundDetector.GetProcessStartTimeUtc(s.ProcessId) ?? DateTime.MinValue;
                var lastUsed = _lastUsed.GetValueOrDefault(s.Key, DateTime.MinValue);
                return new ApplicationIdentity
                {
                    Key = s.Key,
                    DisplayName = s.Name,
                    ExecutablePath = s.ProcessPath,
                    ProcessId = s.ProcessId,
                    IsRunning = true,
                    IsPlaying = s.IsPlaying,
                    IsForeground = isForeground,
                    Volume = (float)s.Volume,
                    IsMuted = s.IsMuted,
                    Priority = Configuration.ActiveProfile.ApplicationPriorities.GetValueOrDefault(s.Key, 100),
                    LastSeenUtc = lastUsed == DateTime.MinValue ? processStarted : lastUsed,
                    StartedUtc = processStarted,
                    LastUsedUtc = lastUsed
                };
            });
            var fixedFirefoxIds = channels
                .Where(channel => channel.IsFixed
                    && channel.Application?.Key.StartsWith("firefox-tab:", StringComparison.Ordinal) == true)
                .Select(channel => channel.Application!.Key["firefox-tab:".Length..])
                .ToHashSet(StringComparer.Ordinal);
            var fixedFirefoxCandidates = Firefox.Tabs
                .Where(tab => fixedFirefoxIds.Contains(tab.Id))
                .Select(tab => new ApplicationIdentity
                {
                    Key = $"firefox-tab:{tab.Id}",
                    DisplayName = tab.Title,
                    ProcessId = tab.NativeTabId,
                    IsRunning = true,
                    IsPlaying = tab.IsPlaying,
                    IsMuted = tab.IsMuted,
                    Volume = (float)tab.Volume,
                    LastSeenUtc = tab.OpenedAtUnixMilliseconds > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(tab.OpenedAtUnixMilliseconds).UtcDateTime
                        : DateTime.UtcNow,
                    StartedUtc = tab.OpenedAtUnixMilliseconds > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(tab.OpenedAtUnixMilliseconds).UtcDateTime
                        : DateTime.MinValue,
                    LastUsedUtc = tab.LastActivatedAtUnixMilliseconds > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(tab.LastActivatedAtUnixMilliseconds).UtcDateTime
                        : DateTime.MinValue
                });
            var candidates = audioCandidates.Concat(fixedFirefoxCandidates).ToList();
            Assignment.Assign(Configuration.ActiveProfile, candidates);
        }

        var current = channels.Select(c => c.Application?.Key).ToArray();
        for (var i = 0; i < current.Length; i++)
        {
            if (!string.Equals(before[i], current[i], StringComparison.OrdinalIgnoreCase))
            {
                _pickupAcquired[i] = false;
                _hasLastAppliedValue[i] = false;
            }
        }
        if (!before.SequenceEqual(current))
        {
            await PersistAsync();
            UpdateLedState();
        }
        AssignmentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleMode()
    {
        if (_stopped) return;
        var requestedMode = Mode == MixerMode.Applications ? MixerMode.Firefox : MixerMode.Applications;
        if (requestedMode == MixerMode.Firefox && !Configuration.ActiveProfile.Firefox.Enabled)
        {
            Logger.Warning("Firefox mode is disabled in the active profile.");
            return;
        }
        Mode = requestedMode;
        Configuration.LastMode = Mode.ToString();
        Array.Clear(_pickupAcquired);
        Array.Clear(_smoothingPrimed);
        Array.Clear(_hasLastAppliedValue);
        _ = PersistModeAsync();
        Logger.Info($"Switched to {Mode} mode.");
        UpdateLedState();
        _ = ReassignAsync();
        ModeChanged?.Invoke(Mode);
    }

    public void ResetChannelInputState(int channelNumber)
    {
        if (channelNumber is < 1 or > HardwareConfig.ChannelCount)
            throw new ArgumentOutOfRangeException(nameof(channelNumber));
        var index = channelNumber - 1;
        _pickupAcquired[index] = false;
        _smoothedValues[index] = 0;
        _smoothingPrimed[index] = false;
        _hasLastAppliedValue[index] = false;
    }

    private void Audio_SessionsChanged(object? sender, EventArgs e)
    {
        if (_stopped) return;
        _ = ApplyProfileAutomationAsync();
        _ = ReassignAsync();
    }

    private async Task PersistModeAsync()
    {
        try { await PersistAsync(); }
        catch (IOException ex) { Logger.Error($"Could not save mixer mode: {ex.Message}"); }
    }

    private async Task ApplyProfileAutomationAsync()
    {
        if (!Configuration.EnableProfileAutomation || _applyingProfileAutomation) return;
        var activeSessionKeys = Audio.Sessions.Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var target = Configuration.ProfileAutomationRules
            .FirstOrDefault(r => r.IsEnabled && activeSessionKeys.Contains(r.ApplicationKey))?.ProfileId
            ?? Configuration.DefaultProfileId;
        if (target == Configuration.ActiveProfileId || !Configuration.Profiles.Any(p => p.Id == target)) return;

        _applyingProfileAutomation = true;
        try { await SetActiveProfileAsync(target); }
        catch (IOException ex) { Logger.Error($"Automatic profile switch could not be saved: {ex.Message}"); }
        finally { _applyingProfileAutomation = false; }
    }
    private void Firefox_TabsChanged(object? sender, EventArgs e)
    {
        if (_stopped) return;
        if (Mode == MixerMode.Firefox || Configuration.ActiveProfile.Channels.Any(channel =>
                channel.IsFixed && channel.Application?.Key.StartsWith("firefox-tab:", StringComparison.Ordinal) == true))
            _ = ReassignAsync();
    }

    private void Firefox_StateChanged(object? sender, EventArgs e)
    {
        Logger.Info(Firefox.IsConnected ? "Firefox native messaging host connected." : "Firefox native messaging host disconnected.");
        if (!_stopped && (Mode == MixerMode.Firefox || Configuration.ActiveProfile.Channels.Any(channel =>
                channel.IsFixed && channel.Application?.Key.StartsWith("firefox-tab:", StringComparison.Ordinal) == true)))
            _ = ReassignAsync();
    }

    private void Arduino_StateChanged(object? sender, EventArgs e)
    {
        if (_stopped) return;
        Logger.Info(Arduino.IsConnected
            ? $"Arduino connected on {Arduino.PortName}."
            : $"Arduino disconnected. {Arduino.LastError}");
    }

    private void Arduino_TouchDetected(object? sender, EventArgs e)
    {
        if (!_stopped && Configuration.ActiveProfile.Firefox.TouchTogglesMode)
            ToggleMode();
    }

    private void Arduino_PotentiometerChanged(object? sender, PotentiometerSample sample)
    {
        if (_stopped) return;
        var channel = Configuration.ActiveProfile.Channels.FirstOrDefault(c => c.Number == sample.Channel);
        if (channel?.Application is not { } app || app.Key.Length == 0) return;

        var index = sample.Channel - 1;
        var value = TransformPotentiometer(sample.RawValue, channel.Calibration, index);
        if (Mode == MixerMode.Firefox && app.Key.StartsWith("firefox-tab:", StringComparison.Ordinal))
        {
            var stableTabId = app.Key["firefox-tab:".Length..];
            var tab = Firefox.Tabs.FirstOrDefault(t => t.Id == stableTabId);
            if (tab is null) return;
            if (channel.Calibration.SoftTakeover && !_pickupAcquired[index])
            {
                if (Math.Abs(value - (tab.IsMuted ? 0 : tab.Volume)) > 0.025) return;
                _pickupAcquired[index] = true;
                Logger.Info($"Potentiometer P{sample.Channel} acquired Firefox tab volume control.");
            }
            if (!ShouldApplyVolume(index, value)) return;
            _ = SetFirefoxTabVolumeAsync(tab.NativeTabId, value);
            VolumeAdjusted?.Invoke(this, new AudioSessionSnapshot(app.Key, tab.Title, "", 0, value, tab.IsMuted, tab.IsPlaying));
            return;
        }

        try
        {
            var current = Audio.Sessions.FirstOrDefault(s =>
                string.Equals(s.Key, app.Key, StringComparison.OrdinalIgnoreCase));
            if (current is null) return;
            if (channel.Calibration.SoftTakeover && !_pickupAcquired[index])
            {
                if (Math.Abs(value - current.Volume) > 0.025) return;
                _pickupAcquired[index] = true;
                Logger.Info($"Potentiometer P{sample.Channel} acquired its volume control.");
            }
            if (!ShouldApplyVolume(index, value)) return;
            Audio.SetVolume(current.Key, value);
            VolumeAdjusted?.Invoke(this, current with { Volume = value });
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Logger.Warning($"Unable to adjust volume on channel {sample.Channel}: {ex.Message}");
        }
    }

    private bool ShouldApplyVolume(int channelIndex, double value)
    {
        if (_hasLastAppliedValue[channelIndex]
            && Math.Abs(value - _lastAppliedValues[channelIndex]) < 0.005)
            return false;
        _lastAppliedValues[channelIndex] = value;
        _hasLastAppliedValue[channelIndex] = true;
        return true;
    }

    private double TransformPotentiometer(int raw, PotentiometerCalibration calibration, int index)
    {
        var value = calibration.Normalize(raw);
        if (!_smoothingPrimed[index])
        {
            _smoothingPrimed[index] = true;
            _smoothedValues[index] = value;
            return value;
        }
        if (calibration.SmoothingPercent > 0)
        {
            var alpha = 1 - Math.Clamp(calibration.SmoothingPercent / 100, 0, 0.95);
            _smoothedValues[index] += alpha * (value - _smoothedValues[index]);
            value = _smoothedValues[index];
        }
        return value;
    }

    public async Task SetFirefoxTabVolumeAsync(int tabId, double volume)
    {
        try { await Firefox.SetTabVolumeAsync(tabId, volume); }
        catch (InvalidOperationException ex) { Logger.Warning($"Firefox tab volume could not be changed: {ex.Message}"); }
        catch (IOException ex) { Logger.Warning($"Firefox tab volume could not be changed: {ex.Message}"); }
    }

    public async Task SetFirefoxTabMutedAsync(int tabId, bool muted)
    {
        try { await Firefox.SetTabMutedAsync(tabId, muted); }
        catch (InvalidOperationException ex) { Logger.Warning($"Firefox tab mute state could not be changed: {ex.Message}"); }
        catch (IOException ex) { Logger.Warning($"Firefox tab mute state could not be changed: {ex.Message}"); }
    }

    private async Task MonitorArduinoConnectionAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (Arduino.IsConnected)
                {
                    try { Arduino.SendPing(); }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException)
                    {
                        Logger.Warning($"Arduino ping failed: {ex.Message}");
                    }
                    continue;
                }
                if (string.IsNullOrWhiteSpace(Configuration.SerialPortName)) continue;
                try
                {
                    Arduino.Connect(Configuration.SerialPortName, Configuration.BaudRate);
                    _lastReconnectError = null;
                    Logger.Info($"Arduino reconnected on {Configuration.SerialPortName}.");
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
                {
                    if (!string.Equals(ex.Message, _lastReconnectError, StringComparison.Ordinal))
                    {
                        Logger.Warning($"Arduino reconnect failed: {ex.Message}");
                        _lastReconnectError = ex.Message;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void UpdateLedState()
    {
        byte mask = 0;
        foreach (var channel in Configuration.ActiveProfile.Channels)
            if (channel.Application is not null)
                mask |= (byte)(1 << (channel.Number - 1));
        try { Arduino.SendLedState(mask, Mode); }
        catch (InvalidOperationException) { }
        catch (IOException ex) { Logger.Warning($"Could not update Arduino LEDs: {ex.Message}"); }
    }
}
