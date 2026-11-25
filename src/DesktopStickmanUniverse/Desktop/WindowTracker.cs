using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DesktopStickmanUniverse.Interop;
using static DesktopStickmanUniverse.Interop.Win32Native;

namespace DesktopStickmanUniverse.Desktop;

/// <summary>
/// Event-driven Windows desktop geometry tracker using SetWinEventHook,
/// DWM extended frame bounds, cloak filtering, and taskbar/monitor inspection.
/// </summary>
public sealed class WindowTracker : IDisposable
{
    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;

    private static readonly HashSet<string> IgnoredWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow",
        "ForegroundStaging",
        "MultitaskingViewFrame",
        "TaskListThumbnailWnd",
        "Xaml_WindowedPopupClass",
        "TopLevelOverflowForXamlIsland",
        "EdgeUiInputTopWndClass",
        "EdgeUiInputWndClass"
    };

    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly List<IntPtr> _winEventHooks = new();
    private WinEventDelegate? _winEventProc;
    private readonly Dispatcher _dispatcher;

    private readonly Dictionary<IntPtr, PixelRect> _previousWindowBounds = new();
    private readonly Dictionary<IntPtr, PixelRect> _previousTaskbarBounds = new();

    private long _sequenceId;
    private volatile bool _dirty = true;
    private DateTime _lastWinEventUtc = DateTime.MinValue;
    private DateTime _lastSnapshotUtc = DateTime.MinValue;
    private bool _disposed;

    public DesktopEnvironmentSnapshot CurrentSnapshot { get; private set; } = DesktopEnvironmentSnapshot.Empty;
    public double LastScanDurationMs { get; private set; }
    public long TotalScans { get; private set; }

    public WindowTracker(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Start()
    {
        if (_winEventHooks.Count > 0)
        {
            return;
        }

        _winEventProc = OnWinEvent;

        // Hook focused set of system/object window events without invasive global input hooks.
        HookRange(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND);
        HookRange(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND);
        HookRange(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND);
        HookRange(EVENT_OBJECT_CREATE, EVENT_OBJECT_HIDE);
        HookRange(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE);
        HookRange(EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE);
        HookRange(EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED);

        RefreshNow();
    }

    private void HookRange(uint min, uint max)
    {
        IntPtr hook = SetWinEventHook(
            min,
            max,
            IntPtr.Zero,
            _winEventProc!,
            0,
            0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        if (hook != IntPtr.Zero)
        {
            _winEventHooks.Add(hook);
        }
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (_disposed || hwnd == IntPtr.Zero)
        {
            return;
        }

        if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
        {
            return;
        }

        if (eventType == EVENT_OBJECT_NAMECHANGE && hwnd != GetForegroundWindow())
        {
            return;
        }

        _dirty = true;
        _lastWinEventUtc = DateTime.UtcNow;
    }

    public void MarkDirty() => _dirty = true;

    /// <summary>
    /// Refreshes the snapshot only when dirty (throttled to avoid scan storms)
    /// or after a low-frequency fallback interval.
    /// </summary>
    public DesktopEnvironmentSnapshot RefreshIfNeeded(bool performanceMode)
    {
        var now = DateTime.UtcNow;
        double minCoalesceMs = performanceMode ? 48.0 : 22.0;
        double fallbackSweepMs = performanceMode ? 2000.0 : 1200.0;

        double sinceLastScanMs = (now - _lastSnapshotUtc).TotalMilliseconds;

        if (_dirty && sinceLastScanMs >= minCoalesceMs)
        {
            return RefreshNow();
        }

        if (sinceLastScanMs >= fallbackSweepMs)
        {
            return RefreshNow();
        }

        return CurrentSnapshot;
    }

    public DesktopEnvironmentSnapshot RefreshNow()
    {
        var sw = Stopwatch.StartNew();
        _dirty = false;
        _lastSnapshotUtc = DateTime.UtcNow;

        var rawMonitors = EnumerateMonitors();
        var taskbars = EnumerateTaskbars(rawMonitors);
        var (windows, fullscreenMonitors, isExclusiveFullscreen) = EnumerateWindows(rawMonitors);

        var finalMonitors = rawMonitors
            .Select(m => m with { HasFullscreenWindow = isExclusiveFullscreen || fullscreenMonitors.Contains(m.DeviceName) })
            .ToList();

        var snapshot = DesktopGeometryBuilder.Build(
            ++_sequenceId,
            finalMonitors,
            taskbars,
            windows,
            _previousWindowBounds,
            _previousTaskbarBounds,
            isExclusiveFullscreen);

        _previousWindowBounds.Clear();
        foreach (var w in windows)
        {
            _previousWindowBounds[w.Hwnd] = w.Bounds;
        }

        _previousTaskbarBounds.Clear();
        foreach (var tb in taskbars)
        {
            _previousTaskbarBounds[tb.Hwnd] = tb.Bounds;
        }

        sw.Stop();
        LastScanDurationMs = sw.Elapsed.TotalMilliseconds;
        TotalScans++;
        CurrentSnapshot = snapshot;
        return snapshot;
    }

    private static List<MonitorInfoData> EnumerateMonitors()
    {
        var monitors = new List<MonitorInfoData>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(hMon, ref mi))
            {
                return true;
            }

            double dpiScale = 1.0;
            try
            {
                if (GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                {
                    dpiScale = dpiX / 96.0;
                }
            }
            catch
            {
                dpiScale = 1.0;
            }

            var bounds = ToPixelRect(mi.rcMonitor);
            var work = ToPixelRect(mi.rcWork);
            bool isPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;

            monitors.Add(new MonitorInfoData(
                string.IsNullOrWhiteSpace(mi.szDevice) ? $"DISPLAY_{monitors.Count}" : mi.szDevice,
                bounds,
                work,
                dpiScale,
                isPrimary,
                false));

            return true;
        }, IntPtr.Zero);

        if (monitors.Count == 0)
        {
            monitors.Add(new MonitorInfoData(
                "DISPLAY_DEFAULT",
                new PixelRect(0, 0, 1920, 1080),
                new PixelRect(0, 0, 1920, 1032),
                1.0,
                true,
                false));
        }

        return monitors;
    }

    private static List<TaskbarInfo> EnumerateTaskbars(IReadOnlyList<MonitorInfoData> monitors)
    {
        var taskbars = new List<TaskbarInfo>();

        var appBarStateData = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        bool isAutoHide = (SHAppBarMessage(ABM_GETSTATE, ref appBarStateData).ToInt64() & ABS_AUTOHIDE) != 0;

        IntPtr primaryHwnd = FindWindow("Shell_TrayWnd", null);
        if (primaryHwnd != IntPtr.Zero)
        {
            var posData = new APPBARDATA
            {
                cbSize = Marshal.SizeOf<APPBARDATA>(),
                hWnd = primaryHwnd
            };

            PixelRect rect;
            TaskbarEdge edge = TaskbarEdge.Bottom;

            if (SHAppBarMessage(ABM_GETTASKBARPOS, ref posData) != IntPtr.Zero)
            {
                rect = ToPixelRect(posData.rc);
                edge = posData.uEdge switch
                {
                    ABE_LEFT => TaskbarEdge.Left,
                    ABE_TOP => TaskbarEdge.Top,
                    ABE_RIGHT => TaskbarEdge.Right,
                    _ => TaskbarEdge.Bottom
                };
            }
            else if (GetWindowRect(primaryHwnd, out var rawRect))
            {
                rect = ToPixelRect(rawRect);
            }
            else
            {
                rect = new PixelRect(0, 0, 0, 0);
            }

            if (GetWindowRect(primaryHwnd, out var liveRect))
            {
                // Live Shell_TrayWnd rect reflects slide-in/out when auto-hide is enabled.
                rect = ToPixelRect(liveRect);
            }

            var mon = FindBestMonitor(monitors, rect) ?? monitors.FirstOrDefault();
            bool visible = IsWindowVisible(primaryHwnd) && IsTaskbarActuallyExposed(rect, edge, mon, isAutoHide);
            taskbars.Add(new TaskbarInfo(
                primaryHwnd,
                rect,
                edge,
                isAutoHide,
                visible,
                mon?.DeviceName ?? string.Empty));
        }

        // Enumerate secondary monitor taskbars on multi-monitor setups.
        EnumWindows((hwnd, _) =>
        {
            string cls = GetWindowClassName(hwnd);
            if (!string.Equals(cls, "Shell_SecondaryTrayWnd", StringComparison.Ordinal))
            {
                return true;
            }

            if (!GetWindowRect(hwnd, out var rawRect))
            {
                return true;
            }

            var rect = ToPixelRect(rawRect);
            var mon = FindBestMonitor(monitors, rect);
            if (mon is null)
            {
                return true;
            }

            TaskbarEdge edge = InferTaskbarEdge(rect, mon.Bounds);
            bool visible = IsWindowVisible(hwnd) && IsTaskbarActuallyExposed(rect, edge, mon, isAutoHide);
            taskbars.Add(new TaskbarInfo(hwnd, rect, edge, isAutoHide, visible, mon.DeviceName));
            return true;
        }, IntPtr.Zero);

        return taskbars;
    }

    private static bool IsTaskbarActuallyExposed(
        PixelRect rect,
        TaskbarEdge edge,
        MonitorInfoData? monitor,
        bool isAutoHide)
    {
        if (rect.IsEmpty || monitor is null)
        {
            return false;
        }

        if (!isAutoHide)
        {
            return true;
        }

        return edge switch
        {
            TaskbarEdge.Bottom => rect.Top < monitor.Bounds.Bottom - 6,
            TaskbarEdge.Top => rect.Bottom > monitor.Bounds.Top + 6,
            TaskbarEdge.Left => rect.Right > monitor.Bounds.Left + 6,
            TaskbarEdge.Right => rect.Left < monitor.Bounds.Right - 6,
            _ => true
        };
    }

    private static TaskbarEdge InferTaskbarEdge(PixelRect rect, PixelRect monBounds)
    {
        if (rect.Width > rect.Height)
        {
            double midY = (rect.Top + rect.Bottom) * 0.5;
            double monMidY = (monBounds.Top + monBounds.Bottom) * 0.5;
            return midY < monMidY ? TaskbarEdge.Top : TaskbarEdge.Bottom;
        }

        double midX = (rect.Left + rect.Right) * 0.5;
        double monMidX = (monBounds.Left + monBounds.Right) * 0.5;
        return midX < monMidX ? TaskbarEdge.Left : TaskbarEdge.Right;
    }

    private (List<TrackedWindow> Windows, HashSet<string> FullscreenMonitors, bool IsExclusiveFullscreen)
        EnumerateWindows(IReadOnlyList<MonitorInfoData> monitors)
    {
        var windows = new List<TrackedWindow>(24);
        var fullscreenMonitors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool isExclusiveFullscreen = false;
        try
        {
            if (SHQueryUserNotificationState(out var state) == 0)
            {
                isExclusiveFullscreen = state is QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
                    or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;
            }
        }
        catch
        {
            isExclusiveFullscreen = false;
        }

        IntPtr foregroundHwnd = GetForegroundWindow();
        int zOrder = 0;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == _ownProcessId)
            {
                return true;
            }

            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((exStyle & WS_EX_TOOLWINDOW) != 0 || (exStyle & WS_EX_NOACTIVATE) != 0)
            {
                return true;
            }

            if (IsWindowCloaked(hwnd))
            {
                return true;
            }

            string className = GetWindowClassName(hwnd);
            if (string.IsNullOrEmpty(className) || IgnoredWindowClasses.Contains(className))
            {
                return true;
            }

            if (!TryGetExtendedWindowRect(hwnd, out var rawRect))
            {
                return true;
            }

            var bounds = ToPixelRect(rawRect);
            if (bounds.Width < 80 || bounds.Height < 48)
            {
                return true;
            }

            var monitor = FindBestMonitor(monitors, bounds);
            if (monitor is null)
            {
                return true;
            }

            bool isForeground = hwnd == foregroundHwnd;
            bool isMaximized = IsZoomed(hwnd);

            // Detect borderless / F11 fullscreen foreground applications without misclassifying
            // normal maximized caption windows when auto-hide taskbar is active.
            if (isForeground && IsLikelyFullscreenWindow(hwnd, bounds, monitor.Bounds, isMaximized))
            {
                fullscreenMonitors.Add(monitor.DeviceName);
            }

            string title = GetWindowTitleText(hwnd);

            windows.Add(new TrackedWindow(
                hwnd,
                bounds,
                title,
                className,
                zOrder++,
                isForeground,
                isMaximized,
                monitor.DeviceName));

            return true;
        }, IntPtr.Zero);

        return (windows, fullscreenMonitors, isExclusiveFullscreen);
    }

    private static bool IsLikelyFullscreenWindow(IntPtr hwnd, PixelRect winBounds, PixelRect monBounds, bool isMaximized)
    {
        bool coversMonitor =
            winBounds.Left <= monBounds.Left + 2 &&
            winBounds.Top <= monBounds.Top + 2 &&
            winBounds.Right >= monBounds.Right - 2 &&
            winBounds.Bottom >= monBounds.Bottom - 2;

        if (!coversMonitor)
        {
            return false;
        }

        if (!isMaximized)
        {
            return true;
        }

        long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        return (style & WS_CAPTION) != WS_CAPTION;
    }

    private static MonitorInfoData? FindBestMonitor(IReadOnlyList<MonitorInfoData> monitors, PixelRect rect)
    {
        double cx = (rect.Left + rect.Right) * 0.5;
        double cy = (rect.Top + rect.Bottom) * 0.5;

        foreach (var mon in monitors)
        {
            if (mon.Bounds.Contains(cx, cy))
            {
                return mon;
            }
        }

        MonitorInfoData? best = null;
        double bestArea = 0;
        foreach (var mon in monitors)
        {
            double ix = Math.Max(0, Math.Min(mon.Bounds.Right, rect.Right) - Math.Max(mon.Bounds.Left, rect.Left));
            double iy = Math.Max(0, Math.Min(mon.Bounds.Bottom, rect.Bottom) - Math.Max(mon.Bounds.Top, rect.Top));
            double area = ix * iy;
            if (area > bestArea)
            {
                bestArea = area;
                best = mon;
            }
        }

        return best;
    }

    private static PixelRect ToPixelRect(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var hook in _winEventHooks)
        {
            if (hook != IntPtr.Zero)
            {
                UnhookWinEvent(hook);
            }
        }

        _winEventHooks.Clear();
        _winEventProc = null;
    }
}
