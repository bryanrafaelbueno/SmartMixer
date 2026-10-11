using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace SmartMixer.Views;

public sealed class DiagnosticsPage : PageBase
{
    private readonly StackPanel _logs = new() { Spacing = 4 };
    private readonly TextBlock _overview = new();

    public DiagnosticsPage() : base("Diagnostics", "Hardware, Windows audio and runtime status.")
    {
        var summary = new StackPanel { Spacing = 12 };
        summary.Children.Add(_overview);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Action("Refresh audio", RefreshAudio));
        actions.Children.Add(Action("Reconnect Arduino", ReconnectArduino));
        actions.Children.Add(Action("Copy diagnostics", CopyDiagnostics));
        summary.Children.Add(actions);
        ContentPanel.Children.Add(Card(summary));

        var audio = Section("Audio sessions");
        foreach (var session in App.Runtime.Audio.Sessions)
        {
            var channel = App.Runtime.Configuration.ActiveProfile.Channels
                .FirstOrDefault(c => string.Equals(c.Application?.Key, session.Key, StringComparison.OrdinalIgnoreCase));
            audio.Children.Add(Card(Muted($"{session.Name} · Volume {session.Volume:P0} · {(session.IsMuted ? "Muted" : session.IsPlaying ? "Playing" : "Idle")} · {(channel is null ? "Unassigned" : $"P{channel.Number}")}")));
        }

        var loggerPanel = new StackPanel { Spacing = 10 };
        loggerPanel.Children.Add(new TextBlock { Text = "Recent logs", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        loggerPanel.Children.Add(_logs);
        var logActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        logActions.Children.Add(Action("Clear logs", (_, _) => { App.Runtime.Logger.Clear(); RenderLogs(); }));
        logActions.Children.Add(Action("Export logs", ExportLogs));
        loggerPanel.Children.Add(logActions);
        ContentPanel.Children.Add(Card(loggerPanel));
        App.Runtime.Logger.EntryAdded += Logger_EntryAdded;
        App.Runtime.Arduino.TelemetryReceived += Arduino_TelemetryReceived;
        App.Runtime.Arduino.StateChanged += Arduino_StateChanged;
        Unloaded += (_, _) => App.Runtime.Logger.EntryAdded -= Logger_EntryAdded;
        Unloaded += (_, _) => App.Runtime.Arduino.TelemetryReceived -= Arduino_TelemetryReceived;
        Unloaded += (_, _) => App.Runtime.Arduino.StateChanged -= Arduino_StateChanged;
        UpdateOverview();
        RenderLogs();
    }

    private void UpdateOverview()
    {
        var runtime = App.Runtime;
        var assigned = runtime.Configuration.ActiveProfile.Channels.Count(c => c.Application is not null);
        var lastPacket = runtime.Arduino.LastPacketUtc is { } packet ? $"{(DateTime.UtcNow - packet).TotalMilliseconds:0} ms ago" : "No packets received";
        _overview.Text =
            $"Arduino: {(runtime.Arduino.IsConnected ? "Connected" : "Disconnected")} · Port: {runtime.Arduino.PortName}\n" +
            $"Communication: {runtime.Arduino.PacketsReceived:N0} received / {runtime.Arduino.PacketsSent:N0} sent · {runtime.Arduino.Errors} errors · {runtime.Arduino.DroppedPackets} dropped · Last packet {lastPacket}\n" +
            $"Round-trip latency: {(runtime.Arduino.AverageLatencyMilliseconds > 0 ? $"{runtime.Arduino.AverageLatencyMilliseconds:0.0} ms" : "Waiting for probe")}\n" +
            $"Audio sessions: {runtime.Audio.Sessions.Count} · Channels: {assigned}/7 assigned\n" +
            $"Firefox extension: {(runtime.Firefox.IsConnected ? "Connected" : "Not connected")} · Native messaging: {(runtime.Firefox.IsConnected ? "Connected" : "Not detected")}\n" +
            $"Windows Audio status: {runtime.Audio.LastError ?? "Healthy"}\n" +
            $"Touch sensor: {(runtime.Arduino.IsConnected ? "Listening" : "Unavailable")} · Touch events: {runtime.Arduino.TouchEvents}\n" +
            $"Firmware: {runtime.Arduino.FirmwareVersion}";
    }

    private async void RefreshAudio(object sender, RoutedEventArgs e)
    {
        try
        {
            await App.Runtime.Audio.RefreshAsync();
            await App.Runtime.ReassignAsync();
            UpdateOverview();
            App.MainWindow?.ShowPage("Diagnostics");
        }
        catch (InvalidOperationException ex)
        {
            App.Runtime.Logger.Error(ex.Message);
            _overview.Text = ex.Message;
        }
    }

    private void ReconnectArduino(object sender, RoutedEventArgs e)
    {
        var port = App.Runtime.Configuration.SerialPortName;
        if (string.IsNullOrWhiteSpace(port))
        {
            _overview.Text = "Select and connect an Arduino port from the Hardware page first.";
            return;
        }
        try { App.Runtime.Arduino.Connect(port, App.Runtime.Configuration.BaudRate); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            App.Runtime.Logger.Error($"Arduino reconnect failed: {ex.Message}");
            _overview.Text = ex.Message;
        }
        UpdateOverview();
    }

    private void CopyDiagnostics(object sender, RoutedEventArgs e)
    {
        UpdateOverview();
        var recentErrors = App.Runtime.Logger.RecentEntries
            .Where(entry => entry.Contains("[ERROR]", StringComparison.Ordinal) || entry.Contains("[WARN]", StringComparison.Ordinal))
            .TakeLast(20)
            .Select(RedactPersonalPaths);
        var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "Unknown";
        var report = $"SmartMixer diagnostics\nApplication version: {version}\nWindows: {RuntimeInformation.OSDescription}\n" +
                     $"{_overview.Text}\nActive profile: {App.Runtime.Configuration.ActiveProfile.Name}\n" +
                     $"Assignment: {App.Runtime.Configuration.ActiveProfile.AssignmentMode} / {App.Runtime.Configuration.ActiveProfile.Ordering}\n" +
                     $"Recent errors:\n{string.Join(Environment.NewLine, recentErrors)}";
        var package = new DataPackage();
        package.SetText(report);
        Clipboard.SetContent(package);
        App.Runtime.Logger.Info("Diagnostics report copied to clipboard.");
    }

    private static string RedactPersonalPaths(string message)
    {
        var sanitized = message;
        foreach (var folder in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            sanitized = sanitized.Replace(folder, "<personal-path>", StringComparison.OrdinalIgnoreCase);
        return sanitized;
    }

    private async void ExportLogs(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.SuggestedFileName = "SmartMixer-logs";
        picker.FileTypeChoices.Add("Log file", [".log", ".txt"]);
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
            await App.Runtime.Logger.ExportAsync(file.Path);
    }

    private void Logger_EntryAdded(object? sender, string entry) => DispatcherQueue.TryEnqueue(RenderLogs);
    private void Arduino_TelemetryReceived(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(UpdateOverview);
    private void Arduino_StateChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(UpdateOverview);

    private void RenderLogs()
    {
        _logs.Children.Clear();
        foreach (var entry in App.Runtime.Logger.RecentEntries.TakeLast(100))
            _logs.Children.Add(new TextBlock { Text = entry, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        UpdateOverview();
    }
}
