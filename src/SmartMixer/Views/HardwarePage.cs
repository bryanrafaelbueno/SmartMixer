using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SmartMixer.Core;

namespace SmartMixer.Views;

public sealed class HardwarePage : PageBase
{
    private readonly StackPanel _liveRows = new() { Spacing = 10 };
    private readonly TextBlock _connectionLabel = new();
    private readonly ComboBox _portPicker = new() { Width = 220 };
    private readonly TextBlock _touchLabel = new();

    public HardwarePage() : base("Hardware", "Arduino Pro Micro connection, live input levels and LED test tools.")
    {
        BuildConnection();
        BuildPotentiometers();
        BuildLedTest();
        BuildTouch();
        App.Runtime.Arduino.StateChanged += Arduino_StateChanged;
        App.Runtime.Arduino.TelemetryReceived += Arduino_TelemetryReceived;
        App.Runtime.Arduino.TouchDetected += Arduino_TouchDetected;
        Unloaded += (_, _) =>
        {
            App.Runtime.Arduino.StateChanged -= Arduino_StateChanged;
            App.Runtime.Arduino.TelemetryReceived -= Arduino_TelemetryReceived;
            App.Runtime.Arduino.TouchDetected -= Arduino_TouchDetected;
        };
        RefreshPortList();
        RenderValues();
    }

    private void BuildConnection()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Arduino Pro Micro", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _connectionLabel.Text = "Disconnected";
        panel.Children.Add(_connectionLabel);
        panel.Children.Add(_portPicker);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Action("Refresh ports", (_, _) => RefreshPortList()));
        actions.Children.Add(Action("Connect", Connect));
        actions.Children.Add(Action("Disconnect", async (_, _) =>
        {
            App.Runtime.Arduino.Disconnect();
            App.Runtime.Configuration.SerialPortName = null;
            await App.Runtime.PersistAsync();
        }));
        panel.Children.Add(actions);
        panel.Children.Add(Muted($"Baud: {HardwareConfig.DefaultBaudRate} · Potentiometers: {string.Join(", ", HardwareConfig.PotentiometerPins)} · LEDs: {string.Join(", ", HardwareConfig.LedPins)} · Touch: {HardwareConfig.TouchPin}"));
        ContentPanel.Children.Add(Card(panel));
    }

    private void BuildPotentiometers()
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Potentiometer test", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        for (var i = 1; i <= HardwareConfig.ChannelCount; i++)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            row.Children.Add(new TextBlock { Text = $"P{i}", VerticalAlignment = VerticalAlignment.Center });
            var bar = new ProgressBar { Maximum = 100, Tag = i, Height = 8, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(bar, 1);
            var value = new TextBlock { Text = "—", Tag = $"value-{i}", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(value, 2);
            row.Children.Add(bar); row.Children.Add(value);
            _liveRows.Children.Add(row);
        }
        panel.Children.Add(_liveRows);
        ContentPanel.Children.Add(Card(panel));
    }

    private void BuildLedTest()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "LED test", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        for (var i = 1; i <= HardwareConfig.ChannelCount; i++)
        {
            var channel = i;
            row.Children.Add(Action($"LED {i}", (_, _) => TestLed(channel)));
        }
        panel.Children.Add(row);
        panel.Children.Add(Action("Test sequence", async (_, _) =>
        {
            for (var i = 1; i <= HardwareConfig.ChannelCount; i++)
            {
                TestLed(i);
                await Task.Delay(220);
            }
        }));
        ContentPanel.Children.Add(Card(panel));
    }

    private void BuildTouch()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Touch sensor", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _touchLabel.Text = $"Waiting · Touch events: {App.Runtime.Arduino.TouchEvents}";
        panel.Children.Add(_touchLabel);
        ContentPanel.Children.Add(Card(panel));
    }

    private void RefreshPortList()
    {
        _portPicker.Items.Clear();
        foreach (var port in App.Runtime.Arduino.GetAvailablePorts()) _portPicker.Items.Add(port);
        var configured = App.Runtime.Configuration.SerialPortName;
        if (!string.IsNullOrWhiteSpace(configured) && _portPicker.Items.Contains(configured))
            _portPicker.SelectedItem = configured;
        else if (_portPicker.Items.Count > 0)
            _portPicker.SelectedIndex = 0;
    }

    private async void Connect(object sender, RoutedEventArgs e)
    {
        if (_portPicker.SelectedItem is not string port) return;
        try
        {
            App.Runtime.Arduino.Connect(port);
            App.Runtime.Configuration.SerialPortName = port;
            App.Runtime.Configuration.BaudRate = HardwareConfig.DefaultBaudRate;
            await App.Runtime.PersistAsync();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
        {
            App.Runtime.Logger.Error($"Arduino connection failed: {ex.Message}");
            _connectionLabel.Text = App.Runtime.Arduino.LastError ?? ex.Message;
        }
    }

    private void TestLed(int channel)
    {
        try { App.Runtime.Arduino.TestLed(channel); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            App.Runtime.Logger.Warning($"LED test failed: {ex.Message}");
            _connectionLabel.Text = ex.Message;
        }
    }

    private void Arduino_StateChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => _connectionLabel.Text = App.Runtime.Arduino.IsConnected
            ? $"Connected · {App.Runtime.Arduino.PortName} · {App.Runtime.Arduino.PacketsReceived:N0} packets"
            : "Disconnected");

    private void Arduino_TelemetryReceived(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(RenderValues);

    private void Arduino_TouchDetected(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => _touchLabel.Text = $"Detected · Touch events: {App.Runtime.Arduino.TouchEvents}");

    private void RenderValues()
    {
        foreach (var element in _liveRows.Children.OfType<Grid>())
        {
            var bar = element.Children.OfType<ProgressBar>().First();
            var index = (int)bar.Tag! - 1;
            var value = App.Runtime.Arduino.PotentiometerValues[index] * 100;
            bar.Value = value;
            element.Children.OfType<TextBlock>().Last().Text = $"{value:0}%";
        }
        _connectionLabel.Text = App.Runtime.Arduino.IsConnected
            ? $"Connected · {App.Runtime.Arduino.PortName} · {App.Runtime.Arduino.PacketsReceived:N0} packets"
            : "Disconnected";
    }
}
