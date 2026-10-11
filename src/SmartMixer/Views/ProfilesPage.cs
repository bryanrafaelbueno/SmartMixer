using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using SmartMixer.Core;
using SmartMixer.Services;
using Windows.Storage.Pickers;

namespace SmartMixer.Views;

public sealed class ProfilesPage : PageBase
{
    public ProfilesPage() : base("Profiles", "Keep separate channel layouts and behaviour for the way you work.")
    {
        var runtime = App.Runtime;
        BuildAutomationRules();
        var add = Action("New profile", async (_, _) =>
        {
            var profile = MixerConfiguration.CreateDefault().Profiles[0];
            profile.Name = $"Profile {runtime.Configuration.Profiles.Count + 1}";
            runtime.Configuration.Profiles.Add(profile);
            await runtime.SetActiveProfileAsync(profile.Id);
            App.MainWindow?.ShowPage("Profiles");
        });
        ContentPanel.Children.Add(add);

        foreach (var profile in runtime.Configuration.Profiles.ToArray())
        {
            var column = new StackPanel { Spacing = 10 };
            var title = new TextBlock { Text = profile.Name, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            var configured = profile.Channels.Count(c => c.Application is not null);
            column.Children.Add(title);
            column.Children.Add(Muted($"{configured} channels configured · {profile.AssignmentMode} assignment"));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            actions.Children.Add(Action(profile.Id == runtime.Configuration.ActiveProfileId ? "Current profile" : "Use profile",
                async (_, _) => await runtime.SetActiveProfileAsync(profile.Id)));
            actions.Children.Add(Action("Duplicate", async (_, _) =>
            {
                var copy = profile.Clone();
                runtime.Configuration.Profiles.Add(copy);
                await runtime.PersistAsync();
                App.MainWindow?.ShowPage("Profiles");
            }));
            actions.Children.Add(Action("Rename", async (_, _) =>
            {
                var dialog = new ContentDialog
                {
                    Title = "Rename profile",
                    PrimaryButtonText = "Save",
                    CloseButtonText = "Cancel",
                    XamlRoot = XamlRoot
                };
                var input = new TextBox { Text = profile.Name };
                dialog.Content = input;
                if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
                {
                    profile.Name = input.Text.Trim();
                    await runtime.PersistAsync();
                    App.MainWindow?.ShowPage("Profiles");
                }
            }));
            actions.Children.Add(Action("Set default", async (_, _) =>
            {
                runtime.Configuration.DefaultProfileId = profile.Id;
                await runtime.PersistAsync();
            }));
            actions.Children.Add(Action("Export", async (_, _) => await ExportProfileAsync(profile)));
            actions.Children.Add(Action("Import profile", async (_, _) =>
            {
                var imported = await ImportProfileAsync();
                if (imported is null) return;
                runtime.Configuration.Profiles.Add(imported);
                await runtime.PersistAsync();
                App.MainWindow?.ShowPage("Profiles");
            }));
            actions.Children.Add(Action("Delete", async (_, _) =>
            {
                if (runtime.Configuration.Profiles.Count <= 1) return;
                runtime.Configuration.Profiles.Remove(profile);
                runtime.Configuration.ProfileAutomationRules.RemoveAll(rule => rule.ProfileId == profile.Id);
                if (runtime.Configuration.ActiveProfileId == profile.Id)
                    runtime.Configuration.ActiveProfileId = runtime.Configuration.Profiles[0].Id;
                if (runtime.Configuration.DefaultProfileId == profile.Id)
                    runtime.Configuration.DefaultProfileId = runtime.Configuration.ActiveProfileId;
                await runtime.PersistAsync();
                App.MainWindow?.ShowPage("Profiles");
            }));
            column.Children.Add(actions);
            ContentPanel.Children.Add(Card(column));
        }
    }

    private void BuildAutomationRules()
    {
        var runtime = App.Runtime;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Automatic profile switching", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var enabled = new ToggleSwitch { Header = "Switch profile when selected applications start", IsOn = runtime.Configuration.EnableProfileAutomation };
        enabled.Toggled += async (_, _) =>
        {
            runtime.Configuration.EnableProfileAutomation = enabled.IsOn;
            await runtime.PersistAsync();
        };
        panel.Children.Add(enabled);
        panel.Children.Add(Muted("The default profile is restored when no enabled rule matches a running audio application."));
        var application = new ComboBox { Header = "Application", Width = 260 };
        foreach (var session in runtime.Audio.Sessions)
            application.Items.Add(new ComboBoxItem { Content = session.Name, Tag = session.Key });
        var target = new ComboBox { Header = "Activate profile", Width = 260 };
        foreach (var profile in runtime.Configuration.Profiles)
            target.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });
        var addRule = Action("Add rule", async (_, _) =>
        {
            if (application.SelectedItem is not ComboBoxItem appItem
                || target.SelectedItem is not ComboBoxItem profileItem
                || appItem.Tag is not string appKey || profileItem.Tag is not string profileId)
                return;
            runtime.Configuration.ProfileAutomationRules.Add(new ProfileAutomationRule { ApplicationKey = appKey, ProfileId = profileId });
            await runtime.PersistAsync();
            App.MainWindow?.ShowPage("Profiles");
        });
        var selectors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        selectors.Children.Add(application); selectors.Children.Add(target); selectors.Children.Add(addRule);
        panel.Children.Add(selectors);

        foreach (var rule in runtime.Configuration.ProfileAutomationRules.ToArray())
        {
            var profileName = runtime.Configuration.Profiles.FirstOrDefault(p => p.Id == rule.ProfileId)?.Name ?? "Missing profile";
            var appName = runtime.Audio.Sessions.FirstOrDefault(s => s.Key == rule.ApplicationKey)?.Name ?? Path.GetFileName(rule.ApplicationKey);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = $"When {appName} is running → {profileName}", VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(Action("Remove", async (_, _) =>
            {
                runtime.Configuration.ProfileAutomationRules.Remove(rule);
                await runtime.PersistAsync();
                App.MainWindow?.ShowPage("Profiles");
            }));
            panel.Children.Add(row);
        }
        ContentPanel.Children.Add(Card(panel));
    }

    private async Task ExportProfileAsync(MixerProfile profile)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.SuggestedFileName = profile.Name;
        picker.FileTypeChoices.Add("SmartMixer profile", [".json"]);
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
            await File.WriteAllTextAsync(file.Path, JsonSerializer.Serialize(profile, ConfigurationStore.JsonOptions()));
    }

    private async Task<MixerProfile?> ImportProfileAsync()
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return null;
        try
        {
            var profile = JsonSerializer.Deserialize<MixerProfile>(
                await File.ReadAllTextAsync(file.Path), ConfigurationStore.JsonOptions())
                ?? throw new InvalidDataException("The selected profile has no data.");
            ConfigurationStore.ValidateProfile(profile);
            profile.Id = Guid.NewGuid().ToString("N");
            return profile;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            App.Runtime.Logger.Error($"Profile import failed: {ex.Message}");
            var dialog = new ContentDialog
            {
                Title = "Profile not imported",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
            return null;
        }
    }
}
