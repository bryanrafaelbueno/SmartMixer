using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using SmartMixer.Services;

namespace SmartMixer.Views;

public sealed class SettingsPage : PageBase
{
    public SettingsPage() : base("Settings", "Personalize appearance, startup and volume feedback.")
    {
        BuildAppearance();
        BuildDetection();
        BuildPotentiometerDirection();
        BuildOverlay();
        BuildStartup();
        BuildConfiguration();
        BuildSetup();
    }

    private void BuildPotentiometerDirection()
    {
        var channels = App.Runtime.Configuration.ActiveProfile.Channels;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Potentiometer direction", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(Muted("Choose which end of each physical control represents 100%."));
        foreach (var channel in channels.OrderBy(channel => channel.Number))
        {
            var toggle = new ToggleSwitch
            {
                Header = $"Channel {channel.Number}: left = 100%",
                IsOn = channel.Calibration.Inverted
            };
            toggle.Toggled += async (_, _) =>
            {
                channel.Calibration.Inverted = toggle.IsOn;
                App.Runtime.ResetChannelInputState(channel.Number);
                await App.Runtime.PersistAsync();
            };
            panel.Children.Add(toggle);
        }
        ContentPanel.Children.Add(Card(panel));
    }

    private void BuildDetection()
    {
        var detection = App.Runtime.Configuration.ActiveProfile.Detection;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Application detection", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        AddToggle(panel, "Detect all audio applications", detection.DetectAllAudioApplications, value => detection.DetectAllAudioApplications = value);
        AddToggle(panel, "Only active applications", detection.OnlyActiveApplications, value => detection.OnlyActiveApplications = value);
        AddToggle(panel, "Only applications producing audio", detection.OnlyPlayingAudio, value => detection.OnlyPlayingAudio = value);
        AddToggle(panel, "Include background applications", detection.IncludeBackgroundApplications, value => detection.IncludeBackgroundApplications = value);
        AddToggle(panel, "Prioritize foreground application", detection.PrioritizeForeground, value => detection.PrioritizeForeground = value);
        panel.Children.Add(Muted("Ignored applications and custom priority are managed from the Applications page."));
        ContentPanel.Children.Add(Card(panel));
    }

    private static void AddToggle(StackPanel panel, string label, bool initial, Action<bool> update)
    {
        var toggle = new ToggleSwitch { Header = label, IsOn = initial };
        toggle.Toggled += async (_, _) =>
        {
            update(toggle.IsOn);
            await App.Runtime.PersistAsync();
            await App.Runtime.ReassignAsync();
        };
        panel.Children.Add(toggle);
    }

    private void BuildAppearance()
    {
        var configuration = App.Runtime.Configuration;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Appearance", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var theme = new ComboBox { Header = "Theme", Width = 240 };
        foreach (var item in new[] { "System", "Light", "Dark" }) theme.Items.Add(item);
        theme.SelectedIndex = configuration.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        theme.SelectionChanged += async (_, _) =>
        {
            if (theme.SelectedItem is string value)
            {
                configuration.Theme = value;
                if (App.MainWindow?.Content is FrameworkElement root)
                    root.RequestedTheme = value switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
                await App.Runtime.PersistAsync();
            }
        };
        panel.Children.Add(theme);
        var compact = new ToggleSwitch { Header = "Compact mode", IsOn = configuration.CompactMode };
        compact.Toggled += async (_, _) =>
        {
            configuration.CompactMode = compact.IsOn;
            await App.Runtime.PersistAsync();
            App.MainWindow?.ShowPage("Settings");
        };
        panel.Children.Add(compact);
        var animations = new ToggleSwitch { Header = "Animations", IsOn = configuration.AnimationsEnabled };
        animations.Toggled += async (_, _) => { configuration.AnimationsEnabled = animations.IsOn; await App.Runtime.PersistAsync(); };
        panel.Children.Add(animations);
        ContentPanel.Children.Add(Card(panel));
    }

    private void BuildOverlay()
    {
        var overlay = App.Runtime.Configuration.ActiveProfile.Overlay;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Volume overlay", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var enabled = new ToggleSwitch { Header = "Show volume overlay", IsOn = overlay.Enabled };
        enabled.Toggled += async (_, _) => { overlay.Enabled = enabled.IsOn; await App.Runtime.PersistAsync(); };
        panel.Children.Add(enabled);
        var showName = new ToggleSwitch { Header = "Show application or tab name", IsOn = overlay.ShowApplicationName };
        showName.Toggled += async (_, _) => { overlay.ShowApplicationName = showName.IsOn; await App.Runtime.PersistAsync(); };
        panel.Children.Add(showName);
        var showIcon = new ToggleSwitch { Header = "Show icon", IsOn = overlay.ShowIcon };
        showIcon.Toggled += async (_, _) => { overlay.ShowIcon = showIcon.IsOn; await App.Runtime.PersistAsync(); };
        panel.Children.Add(showIcon);
        var showPercent = new ToggleSwitch { Header = "Show percentage", IsOn = overlay.ShowPercentage };
        showPercent.Toggled += async (_, _) => { overlay.ShowPercentage = showPercent.IsOn; await App.Runtime.PersistAsync(); };
        panel.Children.Add(showPercent);
        var duration = new Slider { Header = "Duration (seconds)", Minimum = 0.5, Maximum = 5, Value = overlay.DurationSeconds, Width = 360 };
        duration.ValueChanged += async (_, e) => { overlay.DurationSeconds = e.NewValue; await App.Runtime.PersistAsync(); };
        panel.Children.Add(duration);
        var position = new ComboBox { Header = "Position", Width = 240 };
        foreach (var item in new[] { "Bottom", "Top" }) position.Items.Add(item);
        position.SelectedIndex = overlay.Position == "Top" ? 1 : 0;
        position.SelectionChanged += async (_, _) =>
        {
            if (position.SelectedItem is string value)
            {
                overlay.Position = value;
                await App.Runtime.PersistAsync();
            }
        };
        panel.Children.Add(position);
        ContentPanel.Children.Add(Card(panel));
    }

    private async void BuildStartup()
    {
        var configuration = App.Runtime.Configuration;
        var startup = new StartupService();
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Startup and background", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var start = new ToggleSwitch { Header = "Start with Windows", IsOn = configuration.StartWithWindows, IsEnabled = false };
        var startupStatus = new InfoBar { IsOpen = false, Severity = InfoBarSeverity.Warning, IsClosable = true };
        var updatingStartup = false;
        start.Toggled += async (_, _) =>
        {
            if (updatingStartup) return;
            try
            {
                await startup.SetEnabledAsync(start.IsOn);
                configuration.StartWithWindows = start.IsOn;
                await App.Runtime.PersistAsync();
                startupStatus.IsOpen = false;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException or IOException)
            {
                App.Runtime.Logger.Error($"Could not update Windows startup: {ex.Message}");
                startupStatus.Title = "Startup setting was not changed";
                startupStatus.Message = ex.Message;
                startupStatus.IsOpen = true;
                updatingStartup = true;
                try
                {
                    start.IsOn = await startup.IsEnabledAsync();
                }
                catch (Exception stateException) when (stateException is UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
                {
                    App.Runtime.Logger.Error($"Could not read the Windows startup state: {stateException.Message}");
                    start.IsOn = configuration.StartWithWindows;
                }
                finally
                {
                    updatingStartup = false;
                }
            }
        };
        panel.Children.Add(start);
        panel.Children.Add(startupStatus);
        var minimized = new ToggleSwitch { Header = "Start minimized", IsOn = configuration.StartMinimized };
        minimized.Toggled += async (_, _) => { configuration.StartMinimized = minimized.IsOn; await App.Runtime.PersistAsync(); };
        panel.Children.Add(minimized);
        var background = new ToggleSwitch { Header = "Keep mixer running in background", IsOn = configuration.StartInBackground };
        background.Toggled += async (_, _) => { configuration.StartInBackground = background.IsOn; await App.Runtime.PersistAsync(); };
        panel.Children.Add(background);
        ContentPanel.Children.Add(Card(panel));
        updatingStartup = true;
        try
        {
            start.IsOn = await startup.IsEnabledAsync();
            configuration.StartWithWindows = start.IsOn;
            start.IsEnabled = true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
        {
            App.Runtime.Logger.Error($"Could not read the Windows startup state: {ex.Message}");
            startupStatus.Title = "Windows startup state is unavailable";
            startupStatus.Message = ex.Message;
            startupStatus.IsOpen = true;
        }
        finally
        {
            updatingStartup = false;
        }
    }

    private void BuildConfiguration()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Configuration", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(Action("Export configuration", ExportConfiguration));
        panel.Children.Add(Action("Import configuration", ImportConfiguration));
        ContentPanel.Children.Add(Card(panel));
    }

    private void BuildSetup()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Setup assistant", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(Muted("Reopen the guided Arduino, Firefox and potentiometer setup."));
        panel.Children.Add(Action("Run setup assistant", async (_, _) =>
        {
            App.Runtime.Configuration.SetupCompleted = false;
            await App.Runtime.PersistAsync();
            App.MainWindow?.ShowPage("Setup");
        }));
        ContentPanel.Children.Add(Card(panel));
    }

    private async void ExportConfiguration(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.SuggestedFileName = "SmartMixer-configuration";
        picker.FileTypeChoices.Add("JSON configuration", [".json"]);
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
            await App.Runtime.ConfigurationStore.ExportAsync(App.Runtime.Configuration, file.Path);
    }

    private async void ImportConfiguration(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        try
        {
            var configuration = await App.Runtime.ConfigurationStore.ImportAsync(file.Path);
            await App.Runtime.ImportConfigurationAsync(configuration);
            if (App.MainWindow?.Content is FrameworkElement root)
                root.RequestedTheme = configuration.Theme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            App.MainWindow?.ShowPage("Settings");
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            App.Runtime.Logger.Error($"Configuration import failed: {ex.Message}");
            var dialog = new ContentDialog
            {
                Title = "Configuration not imported",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
