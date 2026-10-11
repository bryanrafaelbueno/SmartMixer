using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SmartMixer.Core;

namespace SmartMixer.Views;

public sealed class CalibrationPage : PageBase
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _saveTimer;
    private readonly Dictionary<int, (ChannelConfiguration Channel, TextBlock Values)> _channels = [];

    public CalibrationPage() : base("Hardware calibration", "Calibrate the full travel range of each physical potentiometer.")
    {
        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(350);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += async (_, _) => await App.Runtime.PersistAsync();
        Unloaded += (_, _) =>
        {
            App.Runtime.Arduino.TelemetryReceived -= Arduino_TelemetryReceived;
            if (_saveTimer.IsRunning)
            {
                _saveTimer.Stop();
                _ = App.Runtime.PersistAsync();
            }
        };
        var profile = App.Runtime.Configuration.ActiveProfile;
        App.Runtime.Arduino.TelemetryReceived += Arduino_TelemetryReceived;
        var wizardStep = 0;
        int[]? zeroEndpointReadings = null;
        var wizard = new StackPanel { Spacing = 10 };
        var direction = new ComboBox { Header = "100% position", Width = 260 };
        direction.Items.Add("Turn left");
        direction.Items.Add("Turn right");
        direction.SelectedIndex = 1;
        var stepLabel = new TextBlock { FontSize = 17, TextWrapping = TextWrapping.Wrap };
        var returnToSetup = Action("Return to setup", (_, _) => App.MainWindow?.ShowPage("Setup"));
        returnToSetup.Visibility = Visibility.Collapsed;
        void SetStepText()
        {
            var zeroPosition = direction.SelectedIndex == 0 ? "right" : "left";
            var fullPosition = direction.SelectedIndex == 0 ? "left" : "right";
            stepLabel.Text = wizardStep == 0
                ? $"Step 1 · Turn all potentiometers completely {zeroPosition} (0%)."
                : $"Step 2 · Turn all potentiometers completely {fullPosition} (100%).";
        }
        Button continueButton = null!;
        direction.SelectionChanged += (_, _) =>
        {
            wizardStep = 0;
            zeroEndpointReadings = null;
            continueButton.Content = "Capture 0% position";
            SetStepText();
        };
        continueButton = Action("Capture minimum", async (_, _) =>
        {
            if (!App.Runtime.Arduino.IsConnected)
            {
                stepLabel.Text = "Connect the Arduino before calibrating.";
                return;
            }
            if (!HasFreshTelemetry())
            {
                stepLabel.Text = "Waiting for valid potentiometer readings. Check the Arduino connection and protocol.";
                return;
            }
            if (wizardStep == 0)
            {
                zeroEndpointReadings = App.Runtime.Arduino.RawPotentiometerValues.ToArray();
                if (zeroEndpointReadings.Length != HardwareConfig.ChannelCount)
                {
                    stepLabel.Text = "Seven live potentiometer readings are required.";
                    return;
                }
                wizardStep = 1;
                SetStepText();
                continueButton.Content = "Capture 100% position";
            }
            else
            {
                var zeroReadings = zeroEndpointReadings;
                if (zeroReadings is null || zeroReadings.Length != HardwareConfig.ChannelCount)
                {
                    wizardStep = 0;
                    SetStepText();
                    continueButton.Content = "Capture 0% position";
                    return;
                }
                var fullEndpointReadings = App.Runtime.Arduino.RawPotentiometerValues.ToArray();
                for (var i = 0; i < HardwareConfig.ChannelCount; i++)
                {
                    if (Math.Abs(fullEndpointReadings[i] - zeroReadings[i]) < 16)
                    {
                        stepLabel.Text = $"Channel {i + 1} moved too little between endpoints. Check the potentiometer and try again.";
                        return;
                    }
                }
                for (var i = 0; i < HardwareConfig.ChannelCount; i++)
                {
                    var calibration = profile.Channels.First(c => c.Number == i + 1).Calibration;
                    calibration.RawMinimum = Math.Min(zeroReadings[i], fullEndpointReadings[i]);
                    calibration.RawMaximum = Math.Max(zeroReadings[i], fullEndpointReadings[i]);
                    calibration.Inverted = fullEndpointReadings[i] < zeroReadings[i];
                    App.Runtime.ResetChannelInputState(i + 1);
                }
                App.Runtime.Configuration.SetupCalibrationCompleted = true;
                await App.Runtime.PersistAsync();
                foreach (var channel in profile.Channels)
                    RenderCalibration(channel, _channels[channel.Number].Values);
                stepLabel.Text = $"Calibration complete · turning {(direction.SelectedIndex == 0 ? "left" : "right")} now reaches 100%.";
                continueButton.IsEnabled = false;
                returnToSetup.Visibility = Visibility.Visible;
            }
        });
        continueButton.Content = "Capture 0% position";
        SetStepText();
        wizard.Children.Add(direction);
        wizard.Children.Add(stepLabel);
        wizard.Children.Add(continueButton);
        wizard.Children.Add(returnToSetup);
        ContentPanel.Children.Add(Card(wizard));

        foreach (var channel in profile.Channels.OrderBy(c => c.Number))
            ContentPanel.Children.Add(BuildChannelCalibration(channel));
    }

    private UIElement BuildChannelCalibration(ChannelConfiguration channel)
    {
        var calibration = channel.Calibration;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = $"Channel {channel.Number} · {HardwareConfig.PotentiometerPins[channel.Number - 1]}", FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var values = new TextBlock();
        RenderCalibration(channel, values);
        panel.Children.Add(values);

        var invert = new ToggleSwitch { Header = "Direction: reverse (left = 100%)", IsOn = calibration.Inverted };
        invert.Toggled += async (_, _) =>
        {
            calibration.Inverted = invert.IsOn;
            App.Runtime.ResetChannelInputState(channel.Number);
            RenderCalibration(channel, values);
            await App.Runtime.PersistAsync();
        };
        panel.Children.Add(invert);
        var pickup = new ToggleSwitch { Header = "Soft takeover (recommended)", IsOn = calibration.SoftTakeover };
        pickup.Toggled += async (_, _) => { calibration.SoftTakeover = pickup.IsOn; await App.Runtime.PersistAsync(); };
        panel.Children.Add(pickup);

        var sensitivity = new Slider { Minimum = 0.5, Maximum = 2, Value = calibration.Sensitivity, Header = "Sensitivity" };
        sensitivity.ValueChanged += (_, e) => { calibration.Sensitivity = e.NewValue; SaveSoon(); };
        panel.Children.Add(sensitivity);
        var deadzone = new Slider { Minimum = 0, Maximum = 10, Value = calibration.DeadzonePercent, Header = "Deadzone (%)" };
        deadzone.ValueChanged += (_, e) => { calibration.DeadzonePercent = e.NewValue; SaveSoon(); };
        panel.Children.Add(deadzone);
        var smoothing = new Slider { Minimum = 0, Maximum = 50, Value = calibration.SmoothingPercent, Header = "Smoothing (%)" };
        smoothing.ValueChanged += (_, e) => { calibration.SmoothingPercent = e.NewValue; SaveSoon(); };
        panel.Children.Add(smoothing);
        panel.Children.Add(Action("Capture current as minimum", async (_, _) =>
        {
            if (!App.Runtime.Arduino.IsConnected || !HasFreshTelemetry())
            {
                values.Text = "Connect the Arduino and wait for a live reading before capturing.";
                return;
            }
            var raw = App.Runtime.Arduino.RawPotentiometerValues[channel.Number - 1];
            if (raw >= calibration.RawMaximum)
            {
                values.Text = "Current reading must be below the captured maximum.";
                return;
            }
            calibration.RawMinimum = raw;
            RenderCalibration(channel, values);
            App.Runtime.ResetChannelInputState(channel.Number);
            await App.Runtime.PersistAsync();
        }));
        panel.Children.Add(Action("Capture current as maximum", async (_, _) =>
        {
            if (!App.Runtime.Arduino.IsConnected || !HasFreshTelemetry())
            {
                values.Text = "Connect the Arduino and wait for a live reading before capturing.";
                return;
            }
            var raw = App.Runtime.Arduino.RawPotentiometerValues[channel.Number - 1];
            if (raw <= calibration.RawMinimum)
            {
                values.Text = "Current reading must be above the captured minimum.";
                return;
            }
            calibration.RawMaximum = raw;
            RenderCalibration(channel, values);
            App.Runtime.ResetChannelInputState(channel.Number);
            await App.Runtime.PersistAsync();
        }));
        _channels[channel.Number] = (channel, values);
        return Card(panel);
    }

    private void Arduino_TelemetryReceived(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(RenderReadings);

    private void RenderReadings()
    {
        foreach (var (number, row) in _channels)
        {
            var raw = App.Runtime.Arduino.RawPotentiometerValues[number - 1];
            row.Values.Text = CalibrationSummary(row.Channel.Calibration, raw);
        }
    }

    private static void RenderCalibration(ChannelConfiguration channel, TextBlock values) =>
        values.Text = CalibrationSummary(channel.Calibration);

    private static string CalibrationSummary(PotentiometerCalibration calibration, int? current = null)
    {
        var direction = calibration.Inverted ? "Left = 100%" : "Right = 100%";
        var currentText = current is { } raw
            ? $"    Current: {raw} ({calibration.Normalize(raw) * 100:0}%)"
            : "";
        return $"Raw minimum: {calibration.RawMinimum}    Raw maximum: {calibration.RawMaximum}    {direction}{currentText}";
    }

    private static bool HasFreshTelemetry() =>
        App.Runtime.Arduino.LastPacketUtc is { } lastPacket
        && DateTime.UtcNow - lastPacket < TimeSpan.FromSeconds(1);

    private void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
