using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Microsoft.Win32;
using SmartMixer.Core;
using SmartMixer.Services;
using Windows.Storage.Pickers;

namespace SmartMixer.Views;

public sealed class SetupPage : PageBase
{
    private readonly ComboBox _portPicker = new() { Width = 260 };
    private readonly TextBlock _status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)Application.Current.Resources["MutedTextBrush"]
    };
    private readonly TextBlock _firefoxStatus = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)Application.Current.Resources["MutedTextBrush"]
    };

    public SetupPage() : base("SmartMixer setup", "Connect the hardware, install the Firefox bridge, and calibrate your controls.")
    {
        BuildHardwareSetup();
        BuildFirefoxSetup();
        BuildCalibrationSetup();
        BuildCompletion();
        RefreshPorts();
    }

    private void BuildHardwareSetup()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "1. Arduino Pro Micro", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(Muted("Connect the board with a data-capable USB cable, select its COM port, and connect. To reinstall firmware, double-tap RESET, refresh the list, and select the bootloader COM port."));
        var firmwareTarget = new TextBlock
        {
            Text = "Firmware target: Arduino Micro / ATmega32U4, 16 MHz. Do not flash this image to another board variant.",
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(firmwareTarget);
        panel.Children.Add(_portPicker);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Action("Refresh ports", (_, _) => RefreshPorts()));
        actions.Children.Add(Action("Connect Arduino", ConnectArduino));
        actions.Children.Add(Action("Upload firmware", UploadFirmware));
        panel.Children.Add(actions);
        panel.Children.Add(_status);
        ContentPanel.Children.Add(Card(panel));
    }

    private void BuildFirefoxSetup()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "2. Firefox integration", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(Muted("Register the local SmartMixer bridge, then install the Mozilla-signed SmartMixer extension in Firefox. Firefox does not allow unsigned extensions to be installed persistently or silently."));
        panel.Children.Add(Action("Register Firefox bridge", (_, _) => RegisterFirefoxBridge()));
        var bundledExtension = Path.Combine(AppContext.BaseDirectory, "firefox", "SmartMixer.signed.xpi");
        if (File.Exists(bundledExtension))
            panel.Children.Add(Action("Install bundled signed Firefox extension", (_, _) => OpenFirefoxExtension(bundledExtension)));
        else
            panel.Children.Add(Action("Select signed Firefox .xpi", SelectFirefoxExtension));
        panel.Children.Add(Action("Open Firefox add-ons", (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://addons.mozilla.org/firefox/") { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                App.Runtime.Logger.Error($"Could not open the Firefox add-ons website: {ex.Message}");
                _firefoxStatus.Text = ex.Message;
            }
        }));
        panel.Children.Add(_firefoxStatus);
        var nativeHost = Path.Combine(AppContext.BaseDirectory, "firefox", "native-host", "SmartMixer.NativeHost.exe");
        if (File.Exists(nativeHost))
            RegisterFirefoxBridge();
        ContentPanel.Children.Add(Card(panel));
    }

    private void RegisterFirefoxBridge()
    {
        try
        {
            var path = new FirefoxNativeHostInstaller().Register();
            _firefoxStatus.Text = $"Firefox bridge registered for this Windows account. Manifest: {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
        {
            App.Runtime.Logger.Error($"Firefox bridge setup failed: {ex.Message}");
            _firefoxStatus.Text = ex.Message;
        }
    }

    private void BuildCalibrationSetup()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "3. Calibrate the potentiometers", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(Muted("The calibration wizard captures the 0% and 100% physical ends independently for all seven channels. Choose whether turning left or right should represent 100%."));
        panel.Children.Add(Action("Start calibration", (_, _) => App.MainWindow?.ShowPage("Calibration"), accent: true));
        ContentPanel.Children.Add(Card(panel));
    }

    private void BuildCompletion()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "4. Finish setup", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(Muted("For best results, calibrate before finishing. You can rerun setup later from Settings."));
        var finish = Action("Finish setup", FinishSetup, accent: true);
        finish.IsEnabled = App.Runtime.Configuration.SetupCalibrationCompleted;
        _status.Loaded += (_, _) => finish.IsEnabled = App.Runtime.Configuration.SetupCalibrationCompleted;
        panel.Children.Add(finish);
        panel.Children.Add(Action("Skip for now", SkipSetup));
        ContentPanel.Children.Add(Card(panel));
    }

    private void RefreshPorts()
    {
        var current = _portPicker.SelectedItem as string ?? App.Runtime.Configuration.SerialPortName;
        _portPicker.Items.Clear();
        foreach (var port in App.Runtime.Arduino.GetAvailablePorts())
            _portPicker.Items.Add(port);
        if (!string.IsNullOrWhiteSpace(current) && _portPicker.Items.Contains(current))
            _portPicker.SelectedItem = current;
        else if (_portPicker.Items.Count > 0)
            _portPicker.SelectedIndex = 0;
    }

    private async void ConnectArduino(object sender, RoutedEventArgs e)
    {
        if (_portPicker.SelectedItem is not string port)
        {
            _status.Text = "No serial port is selected. Connect the Arduino and refresh the port list.";
            return;
        }
        try
        {
            if (App.Runtime.Arduino.IsConnected && !string.Equals(App.Runtime.Arduino.PortName, port, StringComparison.OrdinalIgnoreCase))
                App.Runtime.Arduino.Disconnect();
            if (!App.Runtime.Arduino.IsConnected)
                App.Runtime.Arduino.Connect(port);
            App.Runtime.Configuration.SerialPortName = port;
            App.Runtime.Configuration.BaudRate = HardwareConfig.DefaultBaudRate;
            await App.Runtime.PersistAsync();
            _status.Text = $"Arduino connected on {port}. Firmware version: {App.Runtime.Arduino.FirmwareVersion}.";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException or InvalidOperationException)
        {
            App.Runtime.Logger.Error($"Arduino setup connection failed: {ex.Message}");
            _status.Text = App.Runtime.Arduino.LastError ?? ex.Message;
        }
    }

    private async void UploadFirmware(object sender, RoutedEventArgs e)
    {
        if (_portPicker.SelectedItem is not string port)
        {
            _status.Text = "Select the Arduino bootloader COM port before uploading firmware.";
            return;
        }
        var confirmation = new ContentDialog
        {
            Title = "Upload SmartMixer firmware?",
            Content = $"This will replace firmware on {port} with the image built for the Arduino Micro / ATmega32U4 at 16 MHz. Make sure the selected board is that variant and is in its bootloader mode.",
            PrimaryButtonText = "Upload firmware",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            return;

        try
        {
            if (App.Runtime.Arduino.IsConnected)
                App.Runtime.Arduino.Disconnect();
            _status.Text = $"Uploading firmware to {port}…";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var result = await new FirmwareUploader().UploadAsync(port, timeout.Token);
            _status.Text = $"Firmware upload succeeded on {port}. Select its regular COM port and connect.";
            App.Runtime.Logger.Info($"Arduino firmware upload completed on {port}. {result}");
            RefreshPorts();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException)
        {
            App.Runtime.Logger.Error($"Arduino firmware upload failed: {ex.Message}");
            _status.Text = ex.Message;
        }
    }

    private async void SelectFirefoxExtension(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".xpi");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        if (!string.Equals(Path.GetExtension(file.Path), ".xpi", StringComparison.OrdinalIgnoreCase))
        {
            _firefoxStatus.Text = "Select a Mozilla-signed Firefox .xpi package.";
            return;
        }

        OpenFirefoxExtension(file.Path);
    }

    private void OpenFirefoxExtension(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            _firefoxStatus.Text = "Firefox opened the selected add-on. Confirm installation in Firefox to finish.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            App.Runtime.Logger.Error($"Could not open the Firefox extension package: {ex.Message}");
            _firefoxStatus.Text = ex.Message;
        }
    }

    private async void FinishSetup(object sender, RoutedEventArgs e)
    {
        if (!App.Runtime.Configuration.SetupCalibrationCompleted)
        {
            _status.Text = "Complete potentiometer calibration first, or use Skip for now.";
            return;
        }
        App.Runtime.Configuration.SetupCompleted = true;
        await App.Runtime.PersistAsync();
        App.MainWindow?.ShowPage("Dashboard");
    }

    private async void SkipSetup(object sender, RoutedEventArgs e)
    {
        App.Runtime.Configuration.SetupCompleted = true;
        await App.Runtime.PersistAsync();
        App.MainWindow?.ShowPage("Dashboard");
    }
}
