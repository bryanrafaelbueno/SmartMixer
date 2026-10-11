using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;
using Windows.Graphics;
using Windows.Storage.Streams;
using DrawingIcon = System.Drawing.Icon;

namespace SmartMixer.Services;

public sealed class VolumeOverlayWindow : Window
{
    private const int GwlpExStyle = -20;
    private const int GwlStyle = -16;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const long WsCaption = 0x00C00000;
    private const long WsThickFrame = 0x00040000;
    private const long WsSystemMenu = 0x00080000;
    private const long WsMinimizeBox = 0x00020000;
    private const long WsMaximizeBox = 0x00010000;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint MonitorDefaultToNearest = 2;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;
    private const int SurfaceWidth = 256;
    private const int SurfaceHeight = 78;
    private const int ModeSurfaceHeight = 42;
    private const int PositionInsetDips = 12;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private readonly Slider _slider;
    private readonly TextBlock _name;
    private readonly TextBlock _percentage;
    private readonly TextBlock _volumeIcon;
    private readonly Image _applicationIcon;
    private readonly StackPanel _identity;
    private readonly Grid _volumeRow;
    private readonly Border _surface;
    private Storyboard? _activeAnimation;
    private int _animationGeneration;
    private string? _sessionKey;
    private string? _applicationIconPath;
    private IRandomAccessStream? _applicationIconStream;
    private bool _updating;
    private bool _windowClosed;

    public VolumeOverlayWindow()
    {
        Title = "SmartMixer Volume";
        SystemBackdrop = new DesktopAcrylicBackdrop();

        _surface = new Border
        {
            Width = SurfaceWidth,
            Height = SurfaceHeight,
            Background = new SolidColorBrush(Color.FromArgb(238, 44, 44, 44)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 54, 54, 54)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 6, 12, 6)
        };
        var content = new StackPanel { Spacing = 4 };
        _name = new TextBlock
        {
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = SurfaceWidth - 50,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _identity = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _applicationIcon = new Image
        {
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _identity.Children.Add(_applicationIcon);
        _identity.Children.Add(_name);

        _volumeRow = new Grid { ColumnSpacing = 6 };
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        _volumeIcon = new TextBlock
        {
            Text = "\uE767",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _slider = new Slider { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center, MinHeight = 32 };
        _percentage = new TextBlock
        {
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var accentBrush = new SolidColorBrush(
            new Windows.UI.ViewManagement.UISettings().GetColorValue(
                Windows.UI.ViewManagement.UIColorType.Accent));
        _slider.Foreground = accentBrush;
        _slider.Resources["SliderTrackValueFill"] = accentBrush;
        _slider.Resources["SliderTrackValueFillPointerOver"] = accentBrush;
        _slider.Resources["SliderTrackValueFillPressed"] = accentBrush;
        Grid.SetColumn(_volumeIcon, 0);
        Grid.SetColumn(_slider, 1);
        Grid.SetColumn(_percentage, 2);
        _volumeRow.Children.Add(_volumeIcon);
        _volumeRow.Children.Add(_slider);
        _volumeRow.Children.Add(_percentage);
        content.Children.Add(_identity);
        content.Children.Add(_volumeRow);
        _surface.Child = content;
        Content = _surface;
        _slider.ValueChanged += Slider_ValueChanged;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var extendedStyle = GetWindowLongPtr(hwnd, GwlpExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlpExStyle, new IntPtr(extendedStyle | WsExNoActivate | WsExToolWindow));
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        style &= ~(WsCaption | WsThickFrame | WsSystemMenu | WsMinimizeBox | WsMaximizeBox);
        SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(style));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpFrameChanged | SwpNoZOrder | SwpNoMove | SwpNoSize | SwpNoActivate);
        SetNativeCorners(hwnd);
        AppWindow.Resize(new SizeInt32(SurfaceWidth, SurfaceHeight));
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(1700);
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) =>
        {
            if (App.Runtime.Configuration.AnimationsEnabled)
                AnimateOpacity(1, 0, hideWhenComplete: true);
            else
                AppWindow.Hide();
        };
        Closed += (_, _) =>
        {
            _windowClosed = true;
            _timer.Stop();
            _applicationIconStream?.Dispose();
            _applicationIconStream = null;
        };
    }

    public void ShowVolume(string displayName, string sessionKey, double volume, bool muted)
    {
        if (!App.Runtime.Configuration.ActiveProfile.Overlay.Enabled) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            _sessionKey = sessionKey;
            _name.Text = FormatApplicationName(displayName, sessionKey);
            var overlaySettings = App.Runtime.Configuration.ActiveProfile.Overlay;
            _name.Visibility = overlaySettings.ShowApplicationName ? Visibility.Visible : Visibility.Collapsed;
            _applicationIcon.Visibility = overlaySettings.ShowIcon ? Visibility.Visible : Visibility.Collapsed;
            _volumeIcon.Visibility = overlaySettings.ShowIcon ? Visibility.Visible : Visibility.Collapsed;
            _volumeRow.Visibility = Visibility.Visible;
            _percentage.Visibility = overlaySettings.ShowPercentage ? Visibility.Visible : Visibility.Collapsed;
            _surface.Height = SurfaceHeight;
            _volumeIcon.Text = muted || volume <= 0 ? "\uE74F" : "\uE767";
            UpdateApplicationIcon(sessionKey);
            _updating = true;
            _slider.Value = Math.Clamp(volume * 100, 0, 100);
            _percentage.Text = $"{_slider.Value:0}";
            _updating = false;
            _timer.Stop();
            _timer.Interval = TimeSpan.FromSeconds(App.Runtime.Configuration.ActiveProfile.Overlay.DurationSeconds);
            PositionAndShow(SurfaceHeight, overlaySettings.Position);
        });
    }

    public void ShowModeChanged(SmartMixer.Core.MixerMode mode)
    {
        if (!App.Runtime.Configuration.ActiveProfile.Overlay.Enabled) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            _applicationIcon.Visibility = Visibility.Collapsed;
            _name.Text = mode == SmartMixer.Core.MixerMode.Firefox ? "Firefox Mode" : "Mixer Mode";
            _name.Visibility = Visibility.Visible;
            _volumeRow.Visibility = Visibility.Collapsed;
            _surface.Height = ModeSurfaceHeight;
            PositionAndShow(ModeSurfaceHeight, App.Runtime.Configuration.ActiveProfile.Overlay.Position);
        });
    }

    private void PositionAndShow(int surfaceHeight, string position)
    {
        _timer.Stop();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var owner = App.MainWindow is { } mainWindow
            ? WinRT.Interop.WindowNative.GetWindowHandle(mainWindow)
            : hwnd;
        var dpi = GetDpiForWindow(owner);
        var scale = dpi > 0 ? dpi / 96d : 1d;
        var width = (int)Math.Round(SurfaceWidth * scale);
        var height = (int)Math.Round(surfaceHeight * scale);
        AppWindow.Resize(new SizeInt32(width, height));
        var workArea = GetMonitorWorkArea(owner);
        var inset = (int)Math.Round(PositionInsetDips * scale);
        var x = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
        var y = position == "Top"
            ? workArea.Top + inset
            : workArea.Bottom - height - inset;
        y = Math.Max(workArea.Top, y);
        if (!SetWindowPos(hwnd, HwndTopmost, x, y, width, height, SwpNoActivate | SwpShowWindow | SwpNoOwnerZOrder))
        {
            App.Runtime.Logger.Warning($"Could not position the volume overlay: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            return;
        }
        if (App.Runtime.Configuration.AnimationsEnabled)
            AnimateOpacity(0, 1);
        else
        {
            StopAnimation();
            _surface.Opacity = 1;
        }
        _timer.Interval = TimeSpan.FromSeconds(App.Runtime.Configuration.ActiveProfile.Overlay.DurationSeconds);
        _timer.Start();
    }

    private void UpdateApplicationIcon(string sessionKey)
    {
        var path = ResolveApplicationIconPath(sessionKey);
        if (string.Equals(path, _applicationIconPath, StringComparison.OrdinalIgnoreCase))
            return;

        _applicationIconPath = path;
        _applicationIcon.Source = null;
        _applicationIconStream?.Dispose();
        _applicationIconStream = null;
        if (path is not null)
            _ = LoadApplicationIconAsync(path);
    }

    private static string? ResolveApplicationIconPath(string sessionKey)
    {
        var path = sessionKey;
        if (sessionKey.StartsWith("firefox-tab:", StringComparison.Ordinal))
        {
            path = App.Runtime.Audio.Sessions
                .FirstOrDefault(session =>
                    string.Equals(Path.GetFileNameWithoutExtension(session.ProcessPath), "firefox", StringComparison.OrdinalIgnoreCase))
                ?.ProcessPath ?? "";
        }
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
    }

    private static string FormatApplicationName(string displayName, string sessionKey)
    {
        if (sessionKey.StartsWith("firefox-tab:", StringComparison.Ordinal))
            return displayName;

        var processName = Path.GetFileNameWithoutExtension(sessionKey);
        return string.Equals(displayName, processName, StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.CurrentCulture.TextInfo.ToTitleCase(displayName.ToLower(CultureInfo.CurrentCulture))
            : displayName;
    }

    private async Task LoadApplicationIconAsync(string path)
    {
        try
        {
            var bytes = await Task.Run(() =>
            {
                using var icon = DrawingIcon.ExtractAssociatedIcon(path);
                if (icon is null)
                    throw new InvalidOperationException($"No application icon is available for '{path}'.");
                using var bitmap = icon.ToBitmap();
                const double saturation = 0.45;
                for (var y = 0; y < bitmap.Height; y++)
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var color = bitmap.GetPixel(x, y);
                    var gray = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
                    bitmap.SetPixel(x, y, System.Drawing.Color.FromArgb(
                        color.A,
                        (int)Math.Round(gray + (color.R - gray) * saturation),
                        (int)Math.Round(gray + (color.G - gray) * saturation),
                        (int)Math.Round(gray + (color.B - gray) * saturation)));
                }
                using var stream = new MemoryStream();
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return stream.ToArray();
            });

            IRandomAccessStream? stream = new InMemoryRandomAccessStream();
            try
            {
                using (var writer = new DataWriter(stream))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync();
                    _ = writer.DetachStream();
                }
                stream.Seek(0);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                if (string.Equals(path, _applicationIconPath, StringComparison.OrdinalIgnoreCase) && !_windowClosed)
                {
                    _applicationIconStream = stream;
                    _applicationIcon.Source = bitmap;
                    stream = null;
                }
            }
            finally
            {
                stream?.Dispose();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or ExternalException)
        {
            App.Runtime.Logger.Warning($"Could not load the application icon for the volume overlay: {ex.Message}");
        }
    }

    private static RECT GetMonitorWorkArea(IntPtr windowHandle)
    {
        var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        var info = new MONITORINFO { Size = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            return info.WorkArea;

        return new RECT
        {
            Left = 0,
            Top = 0,
            Right = GetSystemMetrics(0),
            Bottom = GetSystemMetrics(1)
        };
    }

    private static void SetNativeCorners(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        var preference = DwmwcpRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        var borderColor = 0x002C2C2C;
        DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(int));
    }

    private void AnimateOpacity(double from, double to, bool hideWhenComplete = false)
    {
        StopAnimation();
        var generation = ++_animationGeneration;
        _surface.Opacity = from;
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(140),
            EnableDependentAnimation = true
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, _surface);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        if (hideWhenComplete)
            storyboard.Completed += (_, _) =>
            {
                if (generation == _animationGeneration)
                    AppWindow.Hide();
            };
        _activeAnimation = storyboard;
        storyboard.Begin();
    }

    private void StopAnimation()
    {
        _activeAnimation?.Stop();
        _activeAnimation = null;
        _animationGeneration++;
    }

    private void Slider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _percentage.Text = $"{e.NewValue:0}";
        if (_updating || _sessionKey is null) return;
        try
        {
            if (_sessionKey.StartsWith("firefox-tab:", StringComparison.Ordinal))
            {
                var stableTabId = _sessionKey["firefox-tab:".Length..];
                var tab = App.Runtime.Firefox.Tabs.FirstOrDefault(t => t.Id == stableTabId)
                    ?? throw new InvalidOperationException("The Firefox tab is no longer available.");
                _ = App.Runtime.SetFirefoxTabVolumeAsync(tab.NativeTabId, e.NewValue / 100);
            }
            else
                App.Runtime.Audio.SetVolume(_sessionKey, e.NewValue / 100);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            App.Runtime.Logger.Warning($"Overlay volume update failed: {ex.Message}");
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow", SetLastError = true)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int index);
    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint Size;
        public RECT Monitor;
        public RECT WorkArea;
        public uint Flags;
    }
}
