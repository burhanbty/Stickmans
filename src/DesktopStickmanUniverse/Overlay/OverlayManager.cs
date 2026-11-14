using DesktopStickmanUniverse.Desktop;
using DesktopStickmanUniverse.Physics;
using DesktopStickmanUniverse.Rendering;
using Microsoft.Win32;
using System.Windows;

namespace DesktopStickmanUniverse.Overlay;

/// <summary>
/// Manages per-monitor transparent overlay windows, DPI/display hotplug transitions,
/// fullscreen suppression, and click-through toggling.
/// </summary>
public sealed class OverlayManager : IDisposable
{
    private readonly Dictionary<string, OverlayWindow> _overlays = new(StringComparer.OrdinalIgnoreCase);
    private readonly CharacterRenderer _renderer = new();
    private bool _disposed;

    public bool IsSessionLocked { get; private set; }
    public int ActiveVisibleCount => _overlays.Values.Count(w => w.Visibility == Visibility.Visible);

    public event Action<OverlayWindow, Point>? MouseLeftDown;
    public event Action<OverlayWindow, Point>? MouseMove;
    public event Action<OverlayWindow, Point>? MouseLeftUp;
    public event Action<OverlayWindow, Point>? MouseRightUp;
    public event Action? DisplayEnvironmentChanged;

    public OverlayManager()
    {
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public void SyncMonitors(DesktopEnvironmentSnapshot snapshot, bool charactersEnabled)
    {
        if (_disposed)
        {
            return;
        }

        var activeDevices = new HashSet<string>(snapshot.Monitors.Select(m => m.DeviceName), StringComparer.OrdinalIgnoreCase);

        // Remove overlays for disconnected monitors.
        foreach (var existingKey in _overlays.Keys.ToList())
        {
            if (!activeDevices.Contains(existingKey))
            {
                var win = _overlays[existingKey];
                DetachEvents(win);
                win.Close();
                _overlays.Remove(existingKey);
            }
        }

        // Create or update overlays for active monitors.
        foreach (var monitor in snapshot.Monitors)
        {
            if (!_overlays.TryGetValue(monitor.DeviceName, out var window))
            {
                window = new OverlayWindow(monitor);
                AttachEvents(window);
                _overlays[monitor.DeviceName] = window;
                window.Show();
            }
            else
            {
                window.ApplyMonitorBounds(monitor);
            }

            bool suppress = IsSessionLocked || !charactersEnabled || monitor.HasFullscreenWindow;
            window.SetSuppressed(suppress);
        }
    }

    public void UpdateClickThrough(string? interactiveMonitorDeviceName)
    {
        foreach (var (deviceName, window) in _overlays)
        {
            bool shouldAllowInput = interactiveMonitorDeviceName is not null &&
                string.Equals(deviceName, interactiveMonitorDeviceName, StringComparison.OrdinalIgnoreCase);
            window.SetClickThrough(!shouldAllowInput);
        }
    }

    public void RenderAll(
        IReadOnlyList<StickmanCharacter> characters,
        IReadOnlyList<ParticleEffect> particles,
        int? hoveredCharacterId,
        bool showParticles)
    {
        foreach (var window in _overlays.Values)
        {
            window.RenderFrame(_renderer, characters, particles, hoveredCharacterId, showParticles);
        }
    }

    public bool AreAllMonitorsSuppressed(DesktopEnvironmentSnapshot snapshot, bool charactersEnabled)
    {
        if (IsSessionLocked || !charactersEnabled || snapshot.Monitors.Count == 0)
        {
            return true;
        }

        return snapshot.Monitors.All(m => m.HasFullscreenWindow);
    }

    public OverlayWindow? GetWindowForMonitor(string deviceName)
        => _overlays.TryGetValue(deviceName, out var win) ? win : null;

    private void AttachEvents(OverlayWindow window)
    {
        window.OverlayMouseLeftDown += OnWindowMouseLeftDown;
        window.OverlayMouseMove += OnWindowMouseMove;
        window.OverlayMouseLeftUp += OnWindowMouseLeftUp;
        window.OverlayMouseRightUp += OnWindowMouseRightUp;
    }

    private void DetachEvents(OverlayWindow window)
    {
        window.OverlayMouseLeftDown -= OnWindowMouseLeftDown;
        window.OverlayMouseMove -= OnWindowMouseMove;
        window.OverlayMouseLeftUp -= OnWindowMouseLeftUp;
        window.OverlayMouseRightUp -= OnWindowMouseRightUp;
    }

    private void OnWindowMouseLeftDown(OverlayWindow win, Point pt) => MouseLeftDown?.Invoke(win, pt);
    private void OnWindowMouseMove(OverlayWindow win, Point pt) => MouseMove?.Invoke(win, pt);
    private void OnWindowMouseLeftUp(OverlayWindow win, Point pt) => MouseLeftUp?.Invoke(win, pt);
    private void OnWindowMouseRightUp(OverlayWindow win, Point pt) => MouseRightUp?.Invoke(win, pt);

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() => DisplayEnvironmentChanged?.Invoke());
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            IsSessionLocked = true;
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
        {
            IsSessionLocked = false;
        }

        Application.Current?.Dispatcher.BeginInvoke(() => DisplayEnvironmentChanged?.Invoke());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;

        foreach (var win in _overlays.Values)
        {
            DetachEvents(win);
            win.Close();
        }

        _overlays.Clear();
    }
}
