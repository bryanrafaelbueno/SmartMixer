using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using SmartMixer.Views;
using Windows.UI;

namespace SmartMixer;

public sealed partial class MainWindow : Window
{
    private readonly TrayManager _tray;
    private bool _shutdownStarted;
    private bool _shutdownComplete;
    private bool _synchronizingNavigation;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.Changed += (_, _) => UpdateTitleBarInsets();
        UpdateTitleBarInsets();
        AppTitleBar.ActualThemeChanged += (_, _) => UpdateTitleBarColors();
        UpdateTitleBarColors();
        _tray = new TrayManager(this);
        AppWindow.Closing += async (_, e) =>
        {
            if (!_tray.IsExiting && App.Runtime.Configuration.StartInBackground)
            {
                e.Cancel = true;
                AppWindow.Hide();
            }
            else if (!_shutdownComplete)
            {
                e.Cancel = true;
                await ExitAsync();
            }
            else
            {
                _tray.Dispose();
            }
        };
        Navigation.SelectedItem = Navigation.MenuItems[0];
        ShowPage("Dashboard");
    }

    private void UpdateTitleBarInsets()
    {
        var titleBar = AppWindow.TitleBar;
        AppTitleBar.Padding = new Thickness(
            titleBar.LeftInset,
            0,
            titleBar.RightInset,
            0);
    }

    private void UpdateTitleBarColors()
    {
        var titleBar = AppWindow.TitleBar;
        var dark = AppTitleBar.ActualTheme == ElementTheme.Dark;
        titleBar.BackgroundColor = Colors.Transparent;
        titleBar.InactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = dark ? Colors.White : Color.FromArgb(255, 32, 32, 32);
        titleBar.ButtonInactiveForegroundColor = dark ? Color.FromArgb(255, 210, 210, 210) : Color.FromArgb(255, 90, 90, 90);
        titleBar.ButtonHoverBackgroundColor = dark
            ? Color.FromArgb(32, 255, 255, 255)
            : Color.FromArgb(24, 0, 0, 0);
        titleBar.ButtonPressedBackgroundColor = dark
            ? Color.FromArgb(48, 255, 255, 255)
            : Color.FromArgb(40, 0, 0, 0);
    }

    internal async Task ExitAsync()
    {
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        _tray.BeginExit();
        await App.Runtime.StopAsync();
        _shutdownComplete = true;
        App.Overlay?.Close();
        Close();
    }

    internal void ShowPage(string page)
    {
        PageFrame.Content = page switch
        {
            "Channels" => new ChannelsPage(),
            "Applications" => new ApplicationsPage(),
            "Profiles" => new ProfilesPage(),
            "Firefox" => new FirefoxPage(),
            "Hardware" => new HardwarePage(),
            "Calibration" => new CalibrationPage(),
            "Setup" => new SetupPage(),
            "Settings" => new SettingsPage(),
            "Diagnostics" => new DiagnosticsPage(),
            _ => new DashboardPage()
        };
        var selectedItem = Navigation.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, page, StringComparison.Ordinal));
        if (selectedItem is not null && !ReferenceEquals(Navigation.SelectedItem, selectedItem))
        {
            _synchronizingNavigation = true;
            Navigation.SelectedItem = selectedItem;
            _synchronizingNavigation = false;
        }
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_synchronizingNavigation) return;
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string page)
            ShowPage(page);
    }
}
