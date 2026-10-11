using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SmartMixer.Core;

namespace SmartMixer.Views;

public sealed class ApplicationsPage : PageBase
{
    private readonly TextBox _search;
    private readonly StackPanel _list = new() { Spacing = 10 };
    private readonly Dictionary<string, SessionRow> _rows = new(StringComparer.OrdinalIgnoreCase);

    public ApplicationsPage() : base("Applications", "Windows audio sessions currently available to SmartMixer.")
    {
        _search = new TextBox { PlaceholderText = "Search applications", Width = 360 };
        _search.TextChanged += (_, _) => Render(_search.Text);
        ContentPanel.Children.Add(_search);
        ContentPanel.Children.Add(_list);

        App.Runtime.Audio.SessionsChanged += Runtime_SessionsChanged;
        App.Runtime.AssignmentsChanged += Runtime_SessionsChanged;
        Unloaded += (_, _) =>
        {
            App.Runtime.Audio.SessionsChanged -= Runtime_SessionsChanged;
            App.Runtime.AssignmentsChanged -= Runtime_SessionsChanged;
        };
        Render("");
    }

    private void Runtime_SessionsChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => Render(_search.Text));

    private void Render(string query)
    {
        var sessions = App.Runtime.Audio.Sessions
            .Where(session => session.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(session => session.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var activeKeys = sessions.Select(session => session.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var staleKey in _rows.Keys.Where(key => !activeKeys.Contains(key)).ToArray())
            _rows.Remove(staleKey);

        _list.Children.Clear();
        foreach (var session in sessions)
        {
            if (!_rows.TryGetValue(session.Key, out var row))
            {
                row = CreateRow(session);
                _rows.Add(session.Key, row);
            }
            UpdateRow(row, session);
            _list.Children.Add(row.Card);
        }

        if (sessions.Length == 0)
            _list.Children.Add(Card(Muted("No matching Windows audio sessions. Start playback or change the search text.")));
    }

    private SessionRow CreateRow(AudioSessionSnapshot session)
    {
        var runtime = App.Runtime;
        var details = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        var assignment = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["MutedTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        var volume = new Slider { Minimum = 0, Maximum = 100 };
        var muted = new ToggleSwitch { Header = "Mute" };
        var ignored = new ToggleSwitch { Header = "Ignore" };
        var priority = new NumberBox
        {
            Header = "Custom priority (lower first)",
            Minimum = 0,
            Maximum = 999,
            Value = runtime.Configuration.ActiveProfile.ApplicationPriorities.GetValueOrDefault(session.Key, 100),
            Width = 220
        };
        var rowState = new SessionRow(session.Key, details, assignment, volume, muted, ignored, priority);

        volume.ValueChanged += (_, args) =>
        {
            if (rowState.IsUpdating) return;
            try { runtime.Audio.SetVolume(rowState.Key, args.NewValue / 100); }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                runtime.Logger.Warning($"Application volume could not be changed: {ex.Message}");
            }
        };
        muted.Toggled += (_, _) =>
        {
            if (rowState.IsUpdating) return;
            try { runtime.Audio.SetMuted(rowState.Key, muted.IsOn); }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                runtime.Logger.Warning($"Application mute state could not be changed: {ex.Message}");
            }
        };
        ignored.Toggled += async (_, _) =>
        {
            if (rowState.IsUpdating) return;
            var ignoredApps = runtime.Configuration.ActiveProfile.Detection.IgnoredApplications;
            if (ignored.IsOn && !ignoredApps.Contains(rowState.Key, StringComparer.OrdinalIgnoreCase))
                ignoredApps.Add(rowState.Key);
            if (!ignored.IsOn)
                ignoredApps.RemoveAll(key => string.Equals(key, rowState.Key, StringComparison.OrdinalIgnoreCase));
            await runtime.PersistAsync();
            await runtime.ReassignAsync();
        };
        priority.ValueChanged += async (_, args) =>
        {
            if (rowState.IsUpdating || !double.IsFinite(args.NewValue)) return;
            runtime.Configuration.ActiveProfile.ApplicationPriorities[rowState.Key] = (int)args.NewValue;
            await runtime.PersistAsync();
            await runtime.ReassignAsync();
        };

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        Grid.SetColumn(assignment, 0);
        Grid.SetColumn(volume, 1);
        Grid.SetColumn(muted, 2);
        Grid.SetColumn(ignored, 3);
        grid.Children.Add(assignment);
        grid.Children.Add(volume);
        grid.Children.Add(muted);
        grid.Children.Add(ignored);

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(details);
        content.Children.Add(grid);
        content.Children.Add(priority);
        rowState.Card = Card(content);
        return rowState;
    }

    private void UpdateRow(SessionRow row, AudioSessionSnapshot session)
    {
        var channel = App.Runtime.Configuration.ActiveProfile.Channels
            .FirstOrDefault(candidate => string.Equals(candidate.Application?.Key, session.Key, StringComparison.OrdinalIgnoreCase));
        row.IsUpdating = true;
        try
        {
            row.Details.Text = $"{session.Name}\n{session.ProcessPath}";
            row.Assignment.Text = channel is null ? "Unassigned" : $"Assigned to P{channel.Number}";
            row.Volume.Value = Math.Clamp(session.Volume * 100, 0, 100);
            row.Muted.IsOn = session.IsMuted;
            row.Ignored.IsOn = App.Runtime.Configuration.ActiveProfile.Detection.IgnoredApplications
                .Contains(session.Key, StringComparer.OrdinalIgnoreCase);
            row.Priority.Value = App.Runtime.Configuration.ActiveProfile.ApplicationPriorities.GetValueOrDefault(session.Key, 100);
        }
        finally
        {
            row.IsUpdating = false;
        }
    }

    private sealed class SessionRow(
        string key,
        TextBlock details,
        TextBlock assignment,
        Slider volume,
        ToggleSwitch muted,
        ToggleSwitch ignored,
        NumberBox priority)
    {
        public string Key { get; } = key;
        public TextBlock Details { get; } = details;
        public TextBlock Assignment { get; } = assignment;
        public Slider Volume { get; } = volume;
        public ToggleSwitch Muted { get; } = muted;
        public ToggleSwitch Ignored { get; } = ignored;
        public NumberBox Priority { get; } = priority;
        public Border Card { get; set; } = null!;
        public bool IsUpdating { get; set; }
    }
}
