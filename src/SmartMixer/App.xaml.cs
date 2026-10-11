using Microsoft.UI.Xaml;
using SmartMixer.Services;

namespace SmartMixer;

public partial class App : Application
{
    public static MixerRuntime Runtime { get; private set; } = null!;
    public static MainWindow? MainWindow { get; private set; }
    public static VolumeOverlayWindow? Overlay { get; private set; }
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        Runtime = new MixerRuntime();
        UnhandledException += (_, args) =>
        {
            try
            {
                Runtime.Logger.Error($"Unhandled WinUI exception: {args.Message} {args.Exception}");
            }
            catch (Exception loggingException) when (loggingException is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                System.Diagnostics.Trace.WriteLine($"Could not record the unhandled WinUI exception: {loggingException}");
            }
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        MainWindow = _window;
        _window.Activate();
        await Runtime.StartAsync();
        Overlay = new VolumeOverlayWindow();
        Runtime.VolumeAdjusted += (_, session) => Overlay.ShowVolume(session.Name, session.Key, session.Volume, session.IsMuted);
        Runtime.ModeChanged += mode => Overlay.ShowModeChanged(mode);
        if (!Runtime.Configuration.SetupCompleted)
            _window.ShowPage("Setup");
        if (Runtime.Configuration.SetupCompleted && Runtime.Configuration.StartMinimized)
            _window.AppWindow.Hide();
    }
}
