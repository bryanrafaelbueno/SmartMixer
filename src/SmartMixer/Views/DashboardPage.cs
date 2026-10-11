using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SmartMixer.Core;
using SmartMixer.Services;
using ApplicationIdentity = SmartMixer.Core.ApplicationIdentity;

namespace SmartMixer.Views;

public sealed class DashboardPage : PageBase
{
    private readonly TextBlock _summary;
    private readonly Dictionary<int, ChannelRow> _channelRows = [];
    private bool _refreshing;
    private bool _detached;

    public DashboardPage() : base("Mixer overview", "Your seven-channel mixer at a glance.")
    {
        var runtime = App.Runtime;
        var overview = new StackPanel { Spacing = 8 };
        var profileName = new TextBlock
        {
            Text = runtime.Configuration.ActiveProfile.Name,
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        _summary = Muted("");
        overview.Children.Add(profileName);
        overview.Children.Add(_summary);
        ContentPanel.Children.Add(Card(overview));

        var channels = Section("Channels");
        var rows = new Grid { ColumnSpacing = 18, RowSpacing = 12 };
        for (var i = 0; i < HardwareConfig.ChannelCount; i++)
            rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });

        foreach (var channel in runtime.Configuration.ActiveProfile.Channels.OrderBy(c => c.Number))
        {
            var index = channel.Number - 1;
            var number = new TextBlock
            {
                Text = $"P{channel.Number}",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var name = new TextBlock { FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            var state = new TextBlock
            {
                Foreground = (Brush)Application.Current.Resources["MutedTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            var slider = new Slider { Minimum = 0, Maximum = 100 };
            var row = new ChannelRow(channel, name, state, slider);
            _channelRows.Add(channel.Number, row);
            slider.ValueChanged += (_, args) =>
            {
                if (_refreshing || channel.Application is not { } application) return;
                try
                {
                    if (FindFirefoxTab(application) is { } tab)
                        _ = runtime.SetFirefoxTabVolumeAsync(tab.NativeTabId, args.NewValue / 100);
                    else
                        runtime.Audio.SetVolume(application.Key, args.NewValue / 100);
                }
                catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                    runtime.Logger.Warning($"Unable to set channel {channel.Number} volume: {ex.Message}");
                }
            };

            Grid.SetRow(number, index); Grid.SetColumn(number, 0);
            Grid.SetRow(name, index); Grid.SetColumn(name, 1);
            Grid.SetRow(state, index); Grid.SetColumn(state, 2);
            Grid.SetRow(slider, index); Grid.SetColumn(slider, 3);
            rows.Children.Add(number); rows.Children.Add(name); rows.Children.Add(state); rows.Children.Add(slider);
        }
        channels.Children.Add(Card(rows));

        var quick = Section("Quick actions");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Action("Channels", (_, _) => App.MainWindow?.ShowPage("Channels")));
        buttons.Children.Add(Action("Profiles", (_, _) => App.MainWindow?.ShowPage("Profiles")));
        buttons.Children.Add(Action("Hardware test", (_, _) => App.MainWindow?.ShowPage("Hardware")));
        quick.Children.Add(buttons);

        runtime.Audio.SessionsChanged += RuntimeStateChanged;
        runtime.AssignmentsChanged += RuntimeStateChanged;
        runtime.Arduino.StateChanged += RuntimeStateChanged;
        runtime.Firefox.StateChanged += RuntimeStateChanged;
        runtime.Firefox.TabsChanged += RuntimeStateChanged;
        Unloaded += (_, _) => Detach(runtime);
        RefreshDashboard();
    }

    private void RuntimeStateChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(RefreshDashboard);

    private void RefreshDashboard()
    {
        var runtime = App.Runtime;
        var profile = runtime.Configuration.ActiveProfile;
        _summary.Text = $"{profile.AssignmentMode} assignment  ·  {runtime.Audio.Sessions.Count} audio sessions  ·  " +
            (runtime.Arduino.IsConnected ? $"Arduino connected on {runtime.Arduino.PortName}" : "Arduino not connected");

        _refreshing = true;
        try
        {
            foreach (var row in _channelRows.Values)
            {
                var application = row.Configuration.Application;
                row.Name.Text = application?.DisplayName ?? "Available";
                row.State.Text = GetChannelState(row.Configuration, out var volume, out var available);
                row.Slider.IsEnabled = application is not null && available;
                row.Slider.Value = Math.Clamp(volume * 100, 0, 100);
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static string GetChannelState(ChannelConfiguration channel, out double volume, out bool available)
    {
        volume = 0;
        available = false;
        if (channel.Application is not { } application)
            return "Available";

        if (FindFirefoxTab(application) is { } tab)
        {
            volume = tab.Volume;
            available = true;
            return channel.IsFixed ? "Fixed" : "Firefox";
        }

        var session = App.Runtime.Audio.Sessions.FirstOrDefault(s =>
            string.Equals(s.Key, application.Key, StringComparison.OrdinalIgnoreCase));
        if (session is null)
            return "Not running";

        volume = session.Volume;
        available = true;
        return channel.IsFixed ? "Fixed" : session.IsPlaying ? "Playing" : "Auto";
    }

    private static FirefoxTab? FindFirefoxTab(ApplicationIdentity application)
    {
        if (application.Key.StartsWith("firefox-tab:", StringComparison.Ordinal))
        {
            var stableId = application.Key["firefox-tab:".Length..];
            return App.Runtime.Firefox.Tabs.FirstOrDefault(candidate => candidate.Id == stableId);
        }
        return null;
    }

    private void Detach(MixerRuntime runtime)
    {
        if (_detached) return;
        _detached = true;
        runtime.Audio.SessionsChanged -= RuntimeStateChanged;
        runtime.AssignmentsChanged -= RuntimeStateChanged;
        runtime.Arduino.StateChanged -= RuntimeStateChanged;
        runtime.Firefox.StateChanged -= RuntimeStateChanged;
        runtime.Firefox.TabsChanged -= RuntimeStateChanged;
    }

    private sealed record ChannelRow(ChannelConfiguration Configuration, TextBlock Name, TextBlock State, Slider Slider);
}
