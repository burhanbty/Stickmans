using System.Threading;
using System.Windows;
using DesktopStickmanUniverse.Core;
using DesktopStickmanUniverse.Settings;
using DesktopStickmanUniverse.UI;

namespace DesktopStickmanUniverse;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private StickmanUniverseEngine? _engine;
    private TrayManager? _trayManager;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, "DesktopStickmanUniverse_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Desktop Stickman Universe is already running in the system tray.",
                "Desktop Stickman Universe",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var settingsService = new SettingsService();
        _engine = new StickmanUniverseEngine(Dispatcher, settingsService);
        _trayManager = new TrayManager();

        _trayManager.UpdatePausedState(_engine.Settings.IsPaused);
        _trayManager.PauseResumeRequested += () => _engine.TogglePause();
        _trayManager.SettingsRequested += ShowOrActivateSettings;
        _trayManager.ScatterRequested += () => _engine.ScatterCharacters();
        _trayManager.MiniEventRequested += kind => _trayManager.ShowMiniEventResult(_engine.TryStartMiniEvent(kind));
        _trayManager.ExitRequested += ShutdownCleanly;

        _engine.OpenSettingsRequested += ShowOrActivateSettings;
        _engine.SettingsChanged += s =>
        {
            _trayManager.UpdatePausedState(s.IsPaused);
            if (_settingsWindow is { IsVisible: true })
            {
                _settingsWindow.SyncFromSettings(s);
            }
        };

        _engine.Start();
    }

    private void ShowOrActivateSettings()
    {
        if (_engine is null)
        {
            return;
        }

        if (_settingsWindow is not null && _settingsWindow.IsLoaded)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_engine);
        _settingsWindow.ExitApplicationRequested += ShutdownCleanly;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShutdownCleanly()
    {
        _settingsWindow?.Close();
        _trayManager?.Dispose();
        _engine?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayManager?.Dispose();
        _engine?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
