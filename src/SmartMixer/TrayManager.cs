using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using SmartMixer.Core;

namespace SmartMixer;

internal sealed class TrayManager : IDisposable
{
    private const uint WmApp = 0x8000;
    private const int WmTray = (int)(WmApp + 41);
    private const int WmContextMenu = 0x007B;
    private const int WmCommand = 0x0111;
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NifMessage = 1;
    private const uint NifIcon = 2;
    private const uint NifTip = 4;
    private const uint TpmRightButton = 2;
    private const uint TpmReturnCommand = 0x0100;
    private const uint MfString = 0;
    private const uint MfSeparator = 0x800;
    private const uint MfPopup = 0x10;
    private const int GwlpWndProc = -4;
    private const int WmClose = 0x0010;
    private const int SwShow = 5;
    private const int SwHide = 0;
    private const uint ImageBitmap = 0;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x0010;
    private static readonly IntPtr IconApplication = new(32512);

    private readonly MainWindow _window;
    private readonly IntPtr _hwnd;
    private readonly WndProcDelegate _callback;
    private readonly IntPtr _originalWndProc;
    private bool _disposed;
    private bool _iconAdded;

    public bool IsExiting { get; private set; }

    internal void BeginExit() => IsExiting = true;

    public TrayManager(MainWindow window)
    {
        _window = window;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _callback = WindowProc;
        _originalWndProc = SetWindowLongPtr(_hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_callback));

        var data = CreateData();
        if (!Shell_NotifyIcon(NimAdd, ref data))
        {
            App.Runtime.Logger.Warning($"Could not create the SmartMixer notification-area icon (Win32 error: {Marshal.GetLastWin32Error()}). Continuing without notification-area icon.");
        }
        else
        {
            _iconAdded = true;
        }
        App.Runtime.Arduino.StateChanged += Runtime_StateChanged;
        App.Runtime.Firefox.StateChanged += Runtime_StateChanged;
    }

    private NOTIFYICONDATA CreateData()
    {
        var iconHandle = IntPtr.Zero;
        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "Images", "SmartMixer.ico");
            if (File.Exists(icoPath))
                iconHandle = LoadImage(IntPtr.Zero, icoPath, ImageIcon, 16, 16, LrLoadFromFile);
        }
        catch { }

        if (iconHandle == IntPtr.Zero)
            iconHandle = LoadIcon(IntPtr.Zero, IconApplication);

        return new()
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmTray,
            hIcon = iconHandle,
            szTip = TrayStatus(),
            szInfo = "",
            szInfoTitle = ""
        };
    }

    private static string TrayStatus()
    {
        var hardware = App.Runtime.Arduino.IsConnected ? "Arduino connected" : "Arduino disconnected";
        var firefox = App.Runtime.Firefox.IsConnected ? "Firefox connected" : "Firefox unavailable";
        return $"SmartMixer · {hardware} · {firefox}";
    }

    private void Runtime_StateChanged(object? sender, EventArgs e)
    {
        _window.DispatcherQueue.TryEnqueue(() =>
        {
            var data = CreateData();
            if (!_iconAdded)
            {
                if (Shell_NotifyIcon(NimAdd, ref data))
                    _iconAdded = true;
                return;
            }
            if (!Shell_NotifyIcon(NimModify, ref data))
                App.Runtime.Logger.Warning("Could not update the SmartMixer notification-area tooltip.");
        });
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmTray && lParam.ToInt32() == 0x0203)
        {
            HandleMenuCommand(1);
            return IntPtr.Zero;
        }
        if (message == WmTray && (lParam.ToInt32() == WmContextMenu || lParam.ToInt32() == 0x0205))
        {
            ShowMenu();
            return IntPtr.Zero;
        }
        if (message == WmCommand)
        {
            HandleMenuCommand(wParam.ToInt32() & 0xFFFF);
            return IntPtr.Zero;
        }
        if (message == WmClose)
        {
            if (!IsExiting)
            {
                ShowWindow(_hwnd, SwHide);
                return IntPtr.Zero;
            }
        }
        return CallWindowProc(_originalWndProc, hwnd, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MfString, 1, "Open Mixer");
        AppendMenu(menu, MfString, 2, "Channels");
        AppendMenu(menu, MfString, 3, "Profiles");
        AppendMenu(menu, MfString, 4, "Settings");
        AppendMenu(menu, MfString, 5, "Diagnostics");
        AppendMenu(menu, MfString, 6, "Reconnect Arduino");
        AppendMenu(menu, MfSeparator, 0, null);
        AppendMenu(menu, MfString, 7, $"Current profile: {App.Runtime.Configuration.ActiveProfile.Name}");
        var profileMenu = CreatePopupMenu();
        var profiles = App.Runtime.Configuration.Profiles;
        for (var i = 0; i < profiles.Count; i++)
            AppendMenu(profileMenu, MfString, 20 + i, profiles[i].Name);
        AppendSubMenu(menu, MfPopup, profileMenu, "Switch profile");
        AppendMenu(menu, MfSeparator, 0, null);
        AppendMenu(menu, MfString, 8, "Exit");
        GetCursorPos(out var point);
        SetForegroundWindow(_hwnd);
        var selected = TrackPopupMenu(menu, TpmRightButton | TpmReturnCommand, point.X, point.Y, 0, _hwnd, IntPtr.Zero);
        if (selected != 0) HandleMenuCommand((int)selected);
        DestroyMenu(menu);
    }

    private void HandleMenuCommand(int command)
    {
        var profileIndex = command - 20;
        if (profileIndex >= 0 && profileIndex < App.Runtime.Configuration.Profiles.Count)
        {
            var profileId = App.Runtime.Configuration.Profiles[profileIndex].Id;
            _ = SwitchProfileAsync(profileId);
            return;
        }
        if (command == 8)
        {
            _ = _window.ExitAsync();
            return;
        }
        if (command == 6)
        {
            try
            {
                var port = App.Runtime.Configuration.SerialPortName;
                if (!string.IsNullOrWhiteSpace(port))
                    App.Runtime.Arduino.Connect(port, App.Runtime.Configuration.BaudRate);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
            {
                App.Runtime.Logger.Error($"Reconnect failed: {ex.Message}");
            }
            return;
        }

        var page = command switch
        {
            2 => "Channels",
            3 => "Profiles",
            4 => "Settings",
            5 => "Diagnostics",
            _ => "Dashboard"
        };
        _window.DispatcherQueue.TryEnqueue(() =>
        {
            _window.ShowPage(page);
            ShowWindow(_hwnd, SwShow);
            _window.Activate();
        });
    }

    private async Task SwitchProfileAsync(string profileId)
    {
        try { await App.Runtime.SetActiveProfileAsync(profileId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            App.Runtime.Logger.Error($"Could not switch profile from the system tray: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        App.Runtime.Arduino.StateChanged -= Runtime_StateChanged;
        App.Runtime.Firefox.StateChanged -= Runtime_StateChanged;
        if (_iconAdded)
        {
            var data = CreateData();
            Shell_NotifyIcon(NimDelete, ref data);
        }
        SetWindowLongPtr(_hwnd, GwlpWndProc, _originalWndProc);
        GC.KeepAlive(_callback);
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "AppendMenuW")]
    private static extern bool AppendMenu(IntPtr menu, uint flags, int itemId, string? text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "AppendMenuW")]
    private static extern bool AppendSubMenu(IntPtr menu, uint flags, IntPtr submenu, string? text);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll", EntryPoint = "LoadIconW", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);
}
