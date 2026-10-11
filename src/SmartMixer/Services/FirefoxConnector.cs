using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SmartMixer.Core;

namespace SmartMixer.Services;

public sealed class FirefoxConnector : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private NamedPipeServerStream? _pipe;
    private Task? _listenTask;
    public IReadOnlyList<FirefoxTab> Tabs { get; private set; } = [];
    public bool IsConnected => _pipe?.IsConnected == true;
    public event EventHandler? StateChanged;
    public event EventHandler? TabsChanged;
    public string? LastError { get; private set; }

    public void Start() => _listenTask ??= ListenAsync(_stop.Token);

    public async Task SetTabVolumeAsync(int tabId, double volume, CancellationToken cancellationToken = default)
    {
        await SendCommandAsync(new { type = "setVolume", tabId, volume = Math.Clamp(volume, 0, 1) }, cancellationToken);
    }

    public async Task SetTabMutedAsync(int tabId, bool muted, CancellationToken cancellationToken = default) =>
        await SendCommandAsync(new { type = "setMuted", tabId, muted }, cancellationToken);

    private async Task SendCommandAsync(object command, CancellationToken cancellationToken)
    {
        var pipe = _pipe;
        if (pipe?.IsConnected != true)
            throw new InvalidOperationException("The Firefox native messaging host is not connected.");
        var message = JsonSerializer.Serialize(command);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            await writer.WriteLineAsync(message.AsMemory(), cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    "SmartMixerFirefox", PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                _pipe = pipe;
                await pipe.WaitForConnectionAsync(cancellationToken);
                LastError = null;
                StateChanged?.Invoke(this, EventArgs.Empty);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
                while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line is null) break;
                    try { HandleMessage(line); }
                    catch (JsonException ex)
                    {
                        LastError = $"Firefox sent an invalid native-messaging payload: {ex.Message}";
                        StateChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (IOException ex) when (!cancellationToken.IsCancellationRequested)
            {
                LastError = ex.Message;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _pipe = null;
                var hadTabs = Tabs.Count > 0;
                Tabs = [];
                if (hadTabs) TabsChanged?.Invoke(this, EventArgs.Empty);
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            try { await Task.Delay(1000, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
        }
    }

    private void HandleMessage(string message)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String
            || type.GetString() != "tabs"
            || !root.TryGetProperty("tabs", out var tabs) || tabs.ValueKind != JsonValueKind.Array)
            return;

        var result = new List<FirefoxTab>();
        foreach (var tab in tabs.EnumerateArray())
        {
            if (tab.ValueKind != JsonValueKind.Object
                || !tab.TryGetProperty("id", out var id) || !id.TryGetInt32(out var nativeTabId)
                || !tab.TryGetProperty("smartMixerId", out var mixerId)
                || mixerId.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(mixerId.GetString())) continue;
            var tabId = mixerId.GetString()!;
            var title = tab.TryGetProperty("title", out var titleNode) && titleNode.ValueKind == JsonValueKind.String
                ? titleNode.GetString() ?? "Untitled tab" : "Untitled tab";
            var domain = tab.TryGetProperty("domain", out var domainNode) && domainNode.ValueKind == JsonValueKind.String
                ? domainNode.GetString() ?? "" : "";
            var favicon = tab.TryGetProperty("favIconUrl", out var iconNode) && iconNode.ValueKind == JsonValueKind.String
                ? iconNode.GetString() : null;
            var playing = tab.TryGetProperty("audible", out var audibleNode) && audibleNode.ValueKind == JsonValueKind.True;
            var muted = tab.TryGetProperty("muted", out var mutedNode) && mutedNode.ValueKind == JsonValueKind.True;
            var volume = tab.TryGetProperty("volume", out var volumeNode) && volumeNode.TryGetDouble(out var value)
                && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;
            var openedAt = tab.TryGetProperty("openedAt", out var openedNode) && openedNode.TryGetInt64(out var openedValue)
                ? openedValue : 0;
            var lastActivatedAt = tab.TryGetProperty("lastActivatedAt", out var activatedNode) && activatedNode.TryGetInt64(out var activatedValue)
                ? activatedValue : 0;
            var active = tab.TryGetProperty("active", out var activeNode) && activeNode.ValueKind == JsonValueKind.True;
            result.Add(new FirefoxTab(tabId, nativeTabId, title, domain, favicon, playing, muted, volume, openedAt, lastActivatedAt, active));
        }
        Tabs = result;
        TabsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _pipe?.Dispose();
        if (_listenTask is not null)
            await _listenTask;
        _stop.Dispose();
    }
}
