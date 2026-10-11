using System.Diagnostics;

namespace SmartMixer.Services;

public sealed class FirmwareUploader
{
    public async Task<string> UploadAsync(string portName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        var baseDirectory = AppContext.BaseDirectory;
        var programmer = Path.Combine(baseDirectory, "tools", "avrdude.exe");
        var configuration = Path.Combine(baseDirectory, "tools", "avrdude.conf");
        var firmware = Path.Combine(baseDirectory, "firmware", "SmartMixerMicro.hex");
        foreach (var file in new[] { programmer, configuration, firmware })
        {
            if (!File.Exists(file))
                throw new FileNotFoundException($"The firmware installation payload is missing '{file}'.", file);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = programmer,
            WorkingDirectory = Path.GetDirectoryName(programmer)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(configuration);
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add("atmega32u4");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("avr109");
        startInfo.ArgumentList.Add("-P");
        startInfo.ArgumentList.Add(portName);
        startInfo.ArgumentList.Add("-b");
        startInfo.ArgumentList.Add("57600");
        startInfo.ArgumentList.Add("-D");
        startInfo.ArgumentList.Add("-U");
        startInfo.ArgumentList.Add($"flash:w:{firmware}:i");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the Arduino firmware programmer.");
        var output = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null) lock (output) output.AppendLine(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null) lock (output) output.AppendLine(args.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            string details;
            lock (output) details = output.ToString().Trim();
            throw new InvalidOperationException(
                $"Firmware upload failed on {portName} with exit code {process.ExitCode}. {details}");
        }

        lock (output) return output.ToString().Trim();
    }
}
