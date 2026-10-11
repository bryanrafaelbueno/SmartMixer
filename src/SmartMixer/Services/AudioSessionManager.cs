using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Globalization;
using System.Runtime.InteropServices;
using SmartMixer.Core;

namespace SmartMixer.Services;

public sealed class AudioSessionManager
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly PeriodicTimer _refreshTimer = new(TimeSpan.FromSeconds(2));
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, MMDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<AudioSessionControl>> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private Task? _refreshLoop;
    private int _disposed;
    private int _resourcesReleased;

    public IReadOnlyList<AudioSessionSnapshot> Sessions { get; private set; } = [];
    public event EventHandler? SessionsChanged;
    public string? LastError { get; private set; }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _refreshLoop ??= RefreshLoopAsync();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            await Task.Run(() => RefreshCore(cancellationToken), cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void RefreshCore(CancellationToken cancellationToken)
    {
        var snapshots = new List<AudioSessionSnapshot>();
        var nextDevices = new Dictionary<string, MMDevice>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var nextSessions = new Dictionary<string, List<AudioSessionControl>>(StringComparer.OrdinalIgnoreCase);
            var snapshotsByApplication = new Dictionary<string, List<AudioSessionSnapshot>>(StringComparer.OrdinalIgnoreCase);
            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                nextDevices[device.ID] = device;
                var sessions = device.AudioSessionManager.Sessions;
                for (var i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    try
                    {
                        var processId = (int)session.GetProcessID;
                        if (processId <= 0) continue;
                        using var process = System.Diagnostics.Process.GetProcessById(processId);
                        var processPath = "";
                        try { processPath = process.MainModule?.FileName ?? ""; }
                        catch (System.ComponentModel.Win32Exception) { }
                        var key = processPath.Length > 0 ? processPath : $"{process.ProcessName}:{processId}";
                        var displayName = string.IsNullOrWhiteSpace(session.DisplayName) ? process.ProcessName : session.DisplayName;
                        if (string.Equals(displayName, process.ProcessName, StringComparison.OrdinalIgnoreCase))
                            displayName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(displayName.ToLower(CultureInfo.CurrentCulture));
                        var active = session.State == AudioSessionState.AudioSessionStateActive;
                        var volume = session.SimpleAudioVolume;
                        if (!nextSessions.TryGetValue(key, out var applicationSessions))
                        {
                            applicationSessions = [];
                            nextSessions.Add(key, applicationSessions);
                            snapshotsByApplication.Add(key, []);
                        }
                        applicationSessions.Add(session);
                        snapshotsByApplication[key].Add(new AudioSessionSnapshot(
                            key, displayName, processPath, processId, volume.Volume, volume.Mute, active));
                    }
                    catch (ArgumentException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (InvalidOperationException) { }
                    catch (COMException) { }
                }
            }
            lock (_sync)
            {
                foreach (var previous in _devices.Values) previous.Dispose();
                _devices.Clear();
                _sessions.Clear();
                foreach (var (id, device) in nextDevices) _devices[id] = device;
                foreach (var (key, sessions) in nextSessions) _sessions[key] = sessions;
            }
            snapshots.AddRange(snapshotsByApplication.Values.Select(group =>
            {
                var first = group[0];
                return first with
                {
                    Volume = group.Average(session => session.Volume),
                    IsMuted = group.All(session => session.IsMuted),
                    IsPlaying = group.Any(session => session.IsPlaying)
                };
            }));
            Sessions = snapshots.OrderBy(snapshot => snapshot.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            LastError = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            foreach (var device in nextDevices.Values) device.Dispose();
            throw new InvalidOperationException("Windows audio sessions could not be enumerated.", ex);
        }
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetVolume(string key, double volume)
    {
        var normalizedVolume = Math.Clamp(volume, 0, 1);
        lock (_sync)
        {
            if (!_sessions.TryGetValue(key, out var sessions))
                throw new KeyNotFoundException($"Audio session '{key}' is no longer available.");
            foreach (var session in sessions)
                session.SimpleAudioVolume.Volume = (float)normalizedVolume;
            UpdateSnapshot(key, snapshot => snapshot with { Volume = normalizedVolume });
        }
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMuted(string key, bool muted)
    {
        lock (_sync)
        {
            if (!_sessions.TryGetValue(key, out var sessions))
                throw new KeyNotFoundException($"Audio session '{key}' is no longer available.");
            foreach (var session in sessions)
                session.SimpleAudioVolume.Mute = muted;
            UpdateSnapshot(key, snapshot => snapshot with { IsMuted = muted });
        }
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSnapshot(string key, Func<AudioSessionSnapshot, AudioSessionSnapshot> update)
    {
        Sessions = Sessions
            .Select(snapshot => string.Equals(snapshot.Key, key, StringComparison.OrdinalIgnoreCase) ? update(snapshot) : snapshot)
            .ToArray();
    }

    private async Task RefreshLoopAsync()
    {
        try
        {
            while (true)
            {
                if (!await _refreshTimer.WaitForNextTickAsync(_stop.Token)) break;
                try { await RefreshAsync(_stop.Token); }
                catch (InvalidOperationException) { SessionsChanged?.Invoke(this, EventArgs.Empty); }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async Task<bool> DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return true;
        _stop.Cancel();
        _refreshTimer.Dispose();
        var refreshLoop = _refreshLoop;
        if (refreshLoop is not null)
        {
            try
            {
                await refreshLoop.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (TimeoutException)
            {
                LastError = "Audio session enumeration is still running; cleanup will finish when it completes.";
                _ = ReleaseResourcesAfterRefreshAsync(refreshLoop);
                return false;
            }
        }

        await ReleaseResourcesAsync();
        return true;
    }

    private async Task ReleaseResourcesAfterRefreshAsync(Task refreshLoop)
    {
        try
        {
            await refreshLoop;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LastError = $"Audio session enumeration failed during shutdown: {ex.Message}";
        }
        finally
        {
            await ReleaseResourcesAsync();
        }
    }

    private async Task ReleaseResourcesAsync()
    {
        if (Interlocked.Exchange(ref _resourcesReleased, 1) != 0) return;
        await _refreshLock.WaitAsync();
        try
        {
            lock (_sync)
            {
                foreach (var device in _devices.Values) device.Dispose();
                _devices.Clear();
                _sessions.Clear();
            }
            _enumerator.Dispose();
        }
        finally
        {
            _refreshLock.Release();
            _stop.Dispose();
        }
    }
}
