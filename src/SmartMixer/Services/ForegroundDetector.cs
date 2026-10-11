using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SmartMixer.Services;

public sealed class ForegroundDetector
{
    public int? GetForegroundProcessId()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        GetWindowThreadProcessId(window, out var processId);
        return processId == 0 ? null : (int)processId;
    }

    public DateTime? GetProcessStartTimeUtc(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
