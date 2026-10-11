using System.Collections.Concurrent;

namespace SmartMixer.Services;

public sealed class StructuredLogger
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartMixer", "logs", "smartmixer.log");
    private readonly ConcurrentQueue<string> _recent = new();
    private readonly object _fileSync = new();
    public event EventHandler<string>? EntryAdded;

    public IReadOnlyList<string> RecentEntries => _recent.ToArray();

    public void Info(string message) => Write("INFO", message);
    public void Warning(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    public void Clear()
    {
        lock (_fileSync)
        {
            while (_recent.TryDequeue(out _)) { }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, "");
        }
        EntryAdded?.Invoke(this, "");
    }

    public async Task ExportAsync(string destination, CancellationToken cancellationToken = default)
    {
        await File.WriteAllLinesAsync(destination, _recent.ToArray(), cancellationToken);
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
        lock (_fileSync)
        {
            _recent.Enqueue(line);
            while (_recent.Count > 1000 && _recent.TryDequeue(out _)) { }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        EntryAdded?.Invoke(this, line);
    }
}
