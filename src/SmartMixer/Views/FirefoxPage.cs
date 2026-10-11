using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SmartMixer.Core;

namespace SmartMixer.Views;

public sealed class FirefoxPage : PageBase
{
    private readonly TextBlock _status = new();
    private readonly StackPanel _tabs = new() { Spacing = 8 };
    private readonly Dictionary<string, TextBlock> _tabAssignments = new(StringComparer.Ordinal);
    private FirefoxTab[] _renderedTabs = [];

    public FirefoxPage() : base("Firefox integration", "Control audio tabs independently using the optional Firefox extension.")
    {
        var profile = App.Runtime.Configuration.ActiveProfile;
        var configuration = profile.Firefox;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Firefox connector", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _status.Text = App.Runtime.Firefox.IsConnected
            ? $"Connected · {App.Runtime.Firefox.Tabs.Count} tabs"
            : "Not connected · install the SmartMixer Firefox extension and native messaging host to enable tab controls.";
        panel.Children.Add(_status);

        var enabled = new ToggleSwitch { Header = "Enable Firefox mode", IsOn = configuration.Enabled };
        enabled.Toggled += async (_, _) => { configuration.Enabled = enabled.IsOn; await App.Runtime.PersistAsync(); };
        panel.Children.Add(enabled);
        var touch = new ToggleSwitch { Header = "Touch sensor switches Application / Firefox mode", IsOn = configuration.TouchTogglesMode };
        touch.Toggled += async (_, _) => { configuration.TouchTogglesMode = touch.IsOn; await App.Runtime.PersistAsync(); };
        panel.Children.Add(touch);
        var ordering = new ComboBox { Header = "Tab ordering", Width = 280 };
        foreach (var item in Enum.GetNames<OrderingMode>()) ordering.Items.Add(item);
        ordering.SelectedIndex = (int)configuration.Ordering;
        ordering.SelectionChanged += async (_, _) =>
        {
            configuration.Ordering = (OrderingMode)Math.Max(0, ordering.SelectedIndex);
            await App.Runtime.PersistAsync();
            if (App.Runtime.Mode == MixerMode.Firefox)
                await App.Runtime.ReassignAsync();
        };
        panel.Children.Add(ordering);
        var count = new NumberBox { Header = "Maximum tabs", Minimum = 1, Maximum = HardwareConfig.ChannelCount, Value = configuration.MaximumTabs, Width = 220 };
        count.ValueChanged += async (_, e) =>
        {
            if (!double.IsFinite(e.NewValue)) return;
            configuration.MaximumTabs = (int)e.NewValue;
            await App.Runtime.PersistAsync();
            if (App.Runtime.Mode == MixerMode.Firefox)
                await App.Runtime.ReassignAsync();
        };
        panel.Children.Add(count);
        ContentPanel.Children.Add(Card(panel));
        var tabsSection = new StackPanel { Spacing = 12 };
        tabsSection.Children.Add(new TextBlock { Text = "Audio tabs", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        tabsSection.Children.Add(_tabs);
        ContentPanel.Children.Add(Card(tabsSection));
        ContentPanel.Children.Add(Card(Muted("Privacy: the companion extension is designed to share only tab identifiers, titles, domains, favicons and audio/mute state. It does not read page content or history.")));
        App.Runtime.Firefox.StateChanged += Firefox_StateChanged;
        App.Runtime.Firefox.TabsChanged += Firefox_TabsChanged;
        App.Runtime.AssignmentsChanged += Runtime_AssignmentsChanged;
        Unloaded += (_, _) =>
        {
            App.Runtime.Firefox.StateChanged -= Firefox_StateChanged;
            App.Runtime.Firefox.TabsChanged -= Firefox_TabsChanged;
            App.Runtime.AssignmentsChanged -= Runtime_AssignmentsChanged;
        };
        RenderTabs();
    }

    private void Firefox_StateChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => _status.Text = App.Runtime.Firefox.IsConnected
            ? $"Connected · {App.Runtime.Firefox.Tabs.Count} tabs"
            : App.Runtime.Firefox.LastError ?? "Not connected · native messaging host is unavailable.");

    private void Firefox_TabsChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_renderedTabs.SequenceEqual(App.Runtime.Firefox.Tabs))
                RenderTabs();
        });

    private void Runtime_AssignmentsChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(RefreshTabAssignments);

    private void RenderTabs()
    {
        var configuration = App.Runtime.Configuration.ActiveProfile.Firefox;
        var tabs = App.Runtime.Firefox.Tabs.ToArray();
        _renderedTabs = tabs;
        _status.Text = App.Runtime.Firefox.IsConnected
            ? $"Connected · {App.Runtime.Firefox.Tabs.Count} tabs"
            : "Not connected · install the Firefox extension and native host.";
        _tabs.Children.Clear();
        _tabAssignments.Clear();
        foreach (var tab in tabs.OrderByDescending(t => t.IsPlaying)
            .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            var row = new StackPanel { Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = $"{tab.Title} · {tab.Domain} · {(tab.IsMuted ? "Muted" : tab.IsPlaying ? "Playing" : "Idle")}",
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var assignment = Muted("");
            _tabAssignments[tab.Id] = assignment;
            row.Children.Add(assignment);
            var volume = new Slider { Minimum = 0, Maximum = 100, Value = tab.Volume * 100 };
            volume.ValueChanged += (_, e) =>
            {
                if (Math.Abs(e.NewValue - tab.Volume * 100) < 0.5) return;
                _ = App.Runtime.SetFirefoxTabVolumeAsync(tab.NativeTabId, e.NewValue / 100);
            };
            row.Children.Add(volume);
            var muted = new ToggleSwitch { Header = "Mute Firefox tab", IsOn = tab.IsMuted };
            muted.Toggled += async (_, _) => await App.Runtime.SetFirefoxTabMutedAsync(tab.NativeTabId, muted.IsOn);
            row.Children.Add(muted);
            var priority = new NumberBox
            {
                Header = "Custom priority (lower first)",
                Minimum = 0,
                Maximum = 999,
                Value = configuration.TabPriorities.GetValueOrDefault(tab.Id, 100),
                Width = 220
            };
            priority.ValueChanged += async (_, e) =>
            {
                if (!double.IsFinite(e.NewValue)) return;
                configuration.TabPriorities[tab.Id] = (int)e.NewValue;
                await App.Runtime.PersistAsync();
                if (configuration.Ordering == OrderingMode.CustomPriority && App.Runtime.Mode == MixerMode.Firefox)
                    await App.Runtime.ReassignAsync();
            };
            row.Children.Add(priority);
            var ignored = new ToggleSwitch
            {
                Header = "Ignore this domain in Firefox mode",
                IsOn = App.Runtime.Configuration.ActiveProfile.Firefox.IgnoredDomains.Contains(tab.Domain, StringComparer.OrdinalIgnoreCase)
            };
            ignored.Toggled += async (_, _) =>
            {
                var domains = App.Runtime.Configuration.ActiveProfile.Firefox.IgnoredDomains;
                if (ignored.IsOn && !domains.Contains(tab.Domain, StringComparer.OrdinalIgnoreCase)) domains.Add(tab.Domain);
                if (!ignored.IsOn) domains.RemoveAll(domain => string.Equals(domain, tab.Domain, StringComparison.OrdinalIgnoreCase));
                await App.Runtime.PersistAsync();
                if (App.Runtime.Mode == MixerMode.Firefox) await App.Runtime.ReassignAsync();
            };
            row.Children.Add(ignored);
            _tabs.Children.Add(Card(row));
        }
        if (_tabs.Children.Count == 0)
            _tabs.Children.Add(Muted("No Firefox audio tabs detected."));
        RefreshTabAssignments();
    }

    private void RefreshTabAssignments()
    {
        foreach (var (tabId, label) in _tabAssignments)
        {
            var channel = App.Runtime.Configuration.ActiveProfile.Channels
                .FirstOrDefault(candidate => candidate.Application?.Key == $"firefox-tab:{tabId}");
            label.Text = channel is null ? "Unassigned" : $"Assigned to P{channel.Number}";
        }
    }
}
