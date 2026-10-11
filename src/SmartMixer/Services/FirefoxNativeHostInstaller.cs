using System.Text.Json;
using Microsoft.Win32;

namespace SmartMixer.Services;

public sealed class FirefoxNativeHostInstaller
{
    public const string ExtensionId = "brthy467@gmail.com";
    private const string HostName = "com.smartmixer.bridge";
    private const string RegistryKeyPath = @"Software\Mozilla\NativeMessagingHosts\com.smartmixer.bridge";

    public string Register()
    {
        var executable = Path.Combine(
            AppContext.BaseDirectory,
            "firefox",
            "native-host",
            "SmartMixer.NativeHost.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("The SmartMixer Firefox native host was not installed with the application.", executable);

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartMixer",
            "FirefoxNativeHost");
        Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, $"{HostName}.json");
        var manifest = new
        {
            name = HostName,
            description = "SmartMixer local Firefox bridge",
            path = executable,
            type = "stdio",
            allowed_extensions = new[] { ExtensionId }
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not register the Firefox native messaging host for this Windows user.");
        key.SetValue(null, manifestPath, RegistryValueKind.String);
        return manifestPath;
    }
}
