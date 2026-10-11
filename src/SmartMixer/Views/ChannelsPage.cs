using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SmartMixer.Core;
using ApplicationIdentity = SmartMixer.Core.ApplicationIdentity;
using Windows.ApplicationModel.DataTransfer;

namespace SmartMixer.Views;

public sealed class ChannelsPage : PageBase
{
    private readonly Dictionary<int, (ChannelConfiguration Channel, TextBlock Status)> _channelStatuses = [];

    public ChannelsPage() : base("Channels", "Assign apps to potentiometers. Drag channel cards to swap their assignments.")
    {
        var profile = App.Runtime.Configuration.ActiveProfile;
        var mode = new ComboBox { Width = 240 };
        foreach (var item in Enum.GetNames<AssignmentMode>()) mode.Items.Add(item);
        mode.SelectedIndex = (int)profile.AssignmentMode;
        mode.SelectionChanged += async (_, _) =>
        {
            profile.AssignmentMode = (AssignmentMode)Math.Max(0, mode.SelectedIndex);
            foreach (var channel in profile.Channels)
            {
                if (profile.AssignmentMode == AssignmentMode.Automatic)
                    channel.IsFixed = false;
                else if (profile.AssignmentMode == AssignmentMode.Fixed)
                    channel.IsFixed = channel.Application is not null;
            }
            await App.Runtime.PersistAsync();
            await App.Runtime.ReassignAsync();
            App.MainWindow?.ShowPage("Channels");
        };
        var modePanel = new StackPanel { Spacing = 10 };
        modePanel.Children.Add(new TextBlock { Text = "Assignment mode", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        modePanel.Children.Add(mode);
        ContentPanel.Children.Add(Card(modePanel));

        foreach (var channel in profile.Channels.OrderBy(c => c.Number))
            ContentPanel.Children.Add(BuildChannelCard(channel));

        var ordering = new ComboBox { Width = 260 };
        foreach (var item in Enum.GetNames<OrderingMode>()) ordering.Items.Add(item);
        ordering.SelectedIndex = (int)profile.Ordering;
        ordering.SelectionChanged += async (_, _) =>
        {
            profile.Ordering = (OrderingMode)Math.Max(0, ordering.SelectedIndex);
            await App.Runtime.PersistAsync();
            await App.Runtime.ReassignAsync();
        };
        var orderPanel = new StackPanel { Spacing = 10 };
        orderPanel.Children.Add(new TextBlock { Text = "Automatic ordering", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        orderPanel.Children.Add(ordering);
        ContentPanel.Children.Add(Card(orderPanel));

        App.Runtime.AssignmentsChanged += Runtime_AssignmentsChanged;
        App.Runtime.Audio.SessionsChanged += Runtime_AssignmentsChanged;
        App.Runtime.Firefox.TabsChanged += Runtime_AssignmentsChanged;
        Unloaded += (_, _) =>
        {
            App.Runtime.AssignmentsChanged -= Runtime_AssignmentsChanged;
            App.Runtime.Audio.SessionsChanged -= Runtime_AssignmentsChanged;
            App.Runtime.Firefox.TabsChanged -= Runtime_AssignmentsChanged;
        };
        RefreshChannelStatuses();
    }

    private UIElement BuildChannelCard(ChannelConfiguration channel)
    {
        var runtime = App.Runtime;
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var no = new TextBlock { Text = $"P{channel.Number}", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var appPicker = new ComboBox { MinWidth = 220, PlaceholderText = "Choose application" };
        foreach (var session in runtime.Audio.Sessions)
            appPicker.Items.Add(new ComboBoxItem { Content = session.Name, Tag = session });
        foreach (var tab in runtime.Firefox.Tabs)
            appPicker.Items.Add(new ComboBoxItem { Content = $"Firefox · {tab.Title}", Tag = tab });
        var fixedToggle = new ToggleSwitch { IsOn = channel.IsFixed, OnContent = "Fixed", OffContent = "Auto" };
        appPicker.IsEnabled = runtime.Configuration.ActiveProfile.AssignmentMode != AssignmentMode.Automatic;
        fixedToggle.IsEnabled = runtime.Configuration.ActiveProfile.AssignmentMode == AssignmentMode.Hybrid;
        var updatingFixed = false;
        var updatingSelection = false;
        fixedToggle.Toggled += async (_, _) =>
        {
            if (updatingFixed) return;
            channel.IsFixed = fixedToggle.IsOn;
            await runtime.PersistAsync();
            await runtime.ReassignAsync();
        };
        var selected = runtime.Audio.Sessions.FirstOrDefault(a =>
            string.Equals(a.Key, channel.Application?.Key, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
            appPicker.SelectedItem = appPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, selected));
        else if (channel.Application?.Key.StartsWith("firefox-tab:", StringComparison.Ordinal) == true)
        {
            var stableId = channel.Application.Key["firefox-tab:".Length..];
            appPicker.SelectedItem = appPicker.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => i.Tag is FirefoxTab tab && tab.Id == stableId);
        }
        appPicker.SelectionChanged += async (_, _) =>
        {
            if (updatingSelection) return;
            var item = appPicker.SelectedItem as ComboBoxItem;
            channel.Application = item?.Tag is AudioSessionSnapshot session
                ? new ApplicationIdentity
                {
                    Key = session.Key,
                    DisplayName = session.Name,
                    ExecutablePath = session.ProcessPath,
                    ProcessId = session.ProcessId,
                    IsRunning = true,
                    IsPlaying = session.IsPlaying
                }
                : item?.Tag is FirefoxTab tab
                    ? new ApplicationIdentity { Key = $"firefox-tab:{tab.Id}", DisplayName = tab.Title, IsRunning = true, IsPlaying = tab.IsPlaying, IsMuted = tab.IsMuted, Volume = (float)tab.Volume }
                    : null;
            if (channel.Application is not null && !channel.IsFixed)
            {
                channel.IsFixed = true;
                updatingFixed = true;
                fixedToggle.IsOn = true;
                updatingFixed = false;
            }
            await runtime.PersistAsync();
            await runtime.ReassignAsync();
        };

        var remove = Action("Remove", async (_, _) =>
        {
            updatingSelection = true;
            channel.Application = null;
            channel.IsFixed = false;
            appPicker.SelectedItem = null;
            updatingFixed = true;
            fixedToggle.IsOn = false;
            updatingFixed = false;
            updatingSelection = false;
            await runtime.PersistAsync();
            await runtime.ReassignAsync();
        });
        var test = Action("Test", (_, _) =>
        {
            if (!runtime.Arduino.IsConnected) return;
            try { runtime.Arduino.TestLed(channel.Number); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { runtime.Logger.Warning(ex.Message); }
        });

        Grid.SetColumn(no, 0); Grid.SetColumn(appPicker, 1); Grid.SetColumn(fixedToggle, 2);
        Grid.SetColumn(remove, 3); Grid.SetColumn(test, 4);
        row.Children.Add(no); row.Children.Add(appPicker); row.Children.Add(fixedToggle); row.Children.Add(remove); row.Children.Add(test);

        var status = Muted("");
        _channelStatuses[channel.Number] = (channel, status);
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(row);
        stack.Children.Add(status);
        var card = Card(stack);
        if (runtime.Configuration.ActiveProfile.AssignmentMode != AssignmentMode.Automatic)
        {
            card.CanDrag = true;
            card.AllowDrop = true;
            card.DragStarting += (_, e) => e.Data.SetText(channel.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
            card.DragOver += (_, e) => e.AcceptedOperation = DataPackageOperation.Move;
            card.Drop += async (_, e) =>
            {
                var text = await e.DataView.GetTextAsync();
                if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var sourceNumber))
                    return;
                var source = runtime.Configuration.ActiveProfile.Channels.FirstOrDefault(candidate => candidate.Number == sourceNumber);
                if (source is null || source.Number == channel.Number) return;
                (source.Application, channel.Application) = (channel.Application, source.Application);
                (source.IsFixed, channel.IsFixed) = (channel.IsFixed, source.IsFixed);
                await runtime.PersistAsync();
                await runtime.ReassignAsync();
                App.MainWindow?.ShowPage("Channels");
            };
        }
        return card;
    }

    private void Runtime_AssignmentsChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(RefreshChannelStatuses);

    private void RefreshChannelStatuses()
    {
        foreach (var (channel, status) in _channelStatuses.Values)
        {
            if (channel.Application is not { } application)
            {
                status.Text = "No application assigned";
                continue;
            }

            if (application.Key.StartsWith("firefox-tab:", StringComparison.Ordinal))
            {
                var stableId = application.Key["firefox-tab:".Length..];
                var tab = App.Runtime.Firefox.Tabs.FirstOrDefault(candidate => candidate.Id == stableId);
                status.Text = tab is null
                    ? $"Firefox tab · {application.DisplayName} · Not running"
                    : $"Firefox tab · {application.DisplayName} · {(tab.IsPlaying ? "Playing" : "Running")}";
                continue;
            }

            var session = App.Runtime.Audio.Sessions.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, application.Key, StringComparison.OrdinalIgnoreCase));
            status.Text = session is null
                ? $"Windows audio session · {application.DisplayName} · Not running"
                : $"Windows audio session · {session.Name} · {(session.IsPlaying ? "Playing" : "Running")}";
        }
    }
}
