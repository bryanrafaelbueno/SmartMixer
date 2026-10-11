using Windows.ApplicationModel;
using Microsoft.Win32;

namespace SmartMixer.Services;

public sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "SmartMixer";

    public async Task<bool> IsEnabledAsync()
    {
        if (!IsPackaged())
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(AppName) is string;
        }

        var task = await StartupTask.GetAsync("SmartMixerStartup");
        return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        if (!IsPackaged())
        {
            SetRegistryEnabled(enabled);
            return;
        }

        var task = await StartupTask.GetAsync("SmartMixerStartup");
        if (!enabled)
        {
            task.Disable();
            return;
        }

        if (task.State is StartupTaskState.DisabledByUser or StartupTaskState.DisabledByPolicy)
            throw new InvalidOperationException($"Windows has disabled SmartMixer startup ({task.State}). Enable it in Windows Settings before trying again.");

        var state = await task.RequestEnableAsync();
        if (state is not StartupTaskState.Enabled and not StartupTaskState.EnabledByPolicy)
            throw new InvalidOperationException($"Windows did not enable SmartMixer at startup ({state}).");
    }

    private static bool IsPackaged()
    {
        try
        {
            return Package.Current is not null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void SetRegistryEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, true)
            ?? throw new InvalidOperationException("Could not access the current user's Windows startup settings.");
        if (enabled)
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Could not determine the SmartMixer executable path.");
            key.SetValue(AppName, $"\"{executable}\"");
        }
        else
            key.DeleteValue(AppName, false);
    }
}
