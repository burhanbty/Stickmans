namespace DesktopStickmanUniverse.Desktop;

public readonly record struct PixelRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;

    public bool Intersects(PixelRect other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
}

public enum TaskbarEdge
{
    Left,
    Top,
    Right,
    Bottom
}

public enum SurfaceKind
{
    MonitorFloor,
    Taskbar,
    WindowTop
}

public enum WallSide
{
    Left,
    Right
}

public sealed record MonitorInfoData(
    string DeviceName,
    PixelRect Bounds,
    PixelRect WorkArea,
    double DpiScale,
    bool IsPrimary,
    bool HasFullscreenWindow);

public sealed record TaskbarInfo(
    IntPtr Hwnd,
    PixelRect Bounds,
    TaskbarEdge Edge,
    bool IsAutoHide,
    bool IsVisible,
    string MonitorDeviceName);

public sealed record TrackedWindow(
    IntPtr Hwnd,
    PixelRect Bounds,
    string Title,
    string ClassName,
    int ZOrder,
    bool IsForeground,
    bool IsMaximized,
    string MonitorDeviceName);

public sealed record WalkableSurface(
    string SurfaceId,
    IntPtr SourceHwnd,
    SurfaceKind Kind,
    double Left,
    double Right,
    double Y,
    double DeltaX,
    double DeltaY,
    double DpiScale,
    int ZOrder,
    string Title)
{
    public double Length => Right - Left;
}

public sealed record ClimbableWall(
    string WallId,
    IntPtr SourceHwnd,
    WallSide Side,
    double X,
    double TopY,
    double BottomY,
    double DeltaX,
    double DeltaY,
    double DpiScale,
    int ZOrder)
{
    public double Height => BottomY - TopY;
}

public sealed record DesktopEnvironmentSnapshot(
    long SequenceId,
    DateTime TimestampUtc,
    IReadOnlyList<MonitorInfoData> Monitors,
    IReadOnlyList<TaskbarInfo> Taskbars,
    IReadOnlyList<TrackedWindow> Windows,
    IReadOnlyList<WalkableSurface> WalkableSurfaces,
    IReadOnlyList<ClimbableWall> ClimbableWalls,
    PixelRect VirtualBounds,
    bool IsAnyExclusiveFullscreen)
{
    public MonitorInfoData? FindMonitorAt(double x, double y)
    {
        foreach (var monitor in Monitors)
        {
            if (monitor.Bounds.Contains(x, y))
            {
                return monitor;
            }
        }

        if (Monitors.Count == 0)
        {
            return null;
        }

        MonitorInfoData best = Monitors[0];
        double bestDistSq = double.MaxValue;
        foreach (var monitor in Monitors)
        {
            double cx = Math.Clamp(x, monitor.Bounds.Left, monitor.Bounds.Right);
            double cy = Math.Clamp(y, monitor.Bounds.Top, monitor.Bounds.Bottom);
            double dx = x - cx;
            double dy = y - cy;
            double distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = monitor;
            }
        }

        return best;
    }

    public double GetDpiScaleAt(double x, double y) => FindMonitorAt(x, y)?.DpiScale ?? 1.0;

    public static DesktopEnvironmentSnapshot Empty { get; } = new(
        0,
        DateTime.UtcNow,
        Array.Empty<MonitorInfoData>(),
        Array.Empty<TaskbarInfo>(),
        Array.Empty<TrackedWindow>(),
        Array.Empty<WalkableSurface>(),
        Array.Empty<ClimbableWall>(),
        new PixelRect(0, 0, 1920, 1080),
        false);
}

/// <summary>
/// Pure geometry builder that converts raw monitors, taskbars, and Z-ordered windows
/// into occluded walkable top edges, climbable vertical walls, and per-HWND movement deltas.
/// </summary>
public static class DesktopGeometryBuilder
{
    private const double MinWalkableSpanLogical = 34.0;
    private const double MinClimbableSpanLogical = 46.0;
    private const double MinTopHeadroomLogical = 38.0;

    public static DesktopEnvironmentSnapshot Build(
        long sequenceId,
        IReadOnlyList<MonitorInfoData> monitors,
        IReadOnlyList<TaskbarInfo> taskbars,
        IReadOnlyList<TrackedWindow> windows,
        IReadOnlyDictionary<IntPtr, PixelRect> previousWindowBounds,
        IReadOnlyDictionary<IntPtr, PixelRect> previousTaskbarBounds,
        bool isExclusiveFullscreen)
    {
        var surfaces = new List<WalkableSurface>(windows.Count * 2 + monitors.Count * 2);
        var walls = new List<ClimbableWall>(windows.Count * 2);

        // 1. Add taskbars (walkable top edge) and monitor floor fallback when taskbar is auto-hidden or absent.
        foreach (var monitor in monitors)
        {
            var matchingTaskbar = taskbars.FirstOrDefault(t =>
                string.Equals(t.MonitorDeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase))
                ?? taskbars.FirstOrDefault(t => t.Bounds.Intersects(monitor.Bounds));

            if (matchingTaskbar is not null && matchingTaskbar.IsVisible && matchingTaskbar.Edge == TaskbarEdge.Bottom)
            {
                double dx = 0;
                double dy = 0;
                if (previousTaskbarBounds.TryGetValue(matchingTaskbar.Hwnd, out var prevTb))
                {
                    dx = matchingTaskbar.Bounds.Left - prevTb.Left;
                    dy = matchingTaskbar.Bounds.Top - prevTb.Top;
                }

                double tbLeft = Math.Max(monitor.Bounds.Left, matchingTaskbar.Bounds.Left);
                double tbRight = Math.Min(monitor.Bounds.Right, matchingTaskbar.Bounds.Right);
                if (tbRight - tbLeft > MinWalkableSpanLogical * monitor.DpiScale)
                {
                    surfaces.Add(new WalkableSurface(
                        $"taskbar:{monitor.DeviceName}",
                        matchingTaskbar.Hwnd,
                        SurfaceKind.Taskbar,
                        tbLeft,
                        tbRight,
                        matchingTaskbar.Bounds.Top,
                        dx,
                        dy,
                        monitor.DpiScale,
                        -1,
                        "Taskbar"));
                }
            }
            else
            {
                double floorY = matchingTaskbar is not null && matchingTaskbar.Edge == TaskbarEdge.Bottom && matchingTaskbar.IsAutoHide
                    ? monitor.Bounds.Bottom
                    : monitor.WorkArea.Bottom;

                surfaces.Add(new WalkableSurface(
                    $"floor:{monitor.DeviceName}",
                    IntPtr.Zero,
                    SurfaceKind.MonitorFloor,
                    monitor.WorkArea.Left,
                    monitor.WorkArea.Right,
                    floorY,
                    0,
                    0,
                    monitor.DpiScale,
                    int.MaxValue,
                    "Desktop Floor"));
            }
        }

        // 2. Process windows in front-to-back Z-order and clip lower windows against occluding upper windows.
        var orderedWindows = windows.OrderBy(w => w.ZOrder).ToList();
        var occluders = new List<PixelRect>(orderedWindows.Count);

        foreach (var win in orderedWindows)
        {
            var monitor = FindContainingMonitor(monitors, win.Bounds);
            double dpi = monitor?.DpiScale ?? 1.0;

            double deltaX = 0;
            double deltaY = 0;
            if (previousWindowBounds.TryGetValue(win.Hwnd, out var prevBounds))
            {
                deltaX = win.Bounds.Left - prevBounds.Left;
                deltaY = win.Bounds.Top - prevBounds.Top;
            }

            // Skip top edge if window reaches near the top of its monitor so stickmen don't walk off-screen.
            double minTopY = (monitor?.Bounds.Top ?? 0) + MinTopHeadroomLogical * dpi;
            if (win.Bounds.Top >= minTopY)
            {
                var visibleTopSegments = ClipHorizontalSegment(
                    win.Bounds.Left + 4 * dpi,
                    win.Bounds.Right - 4 * dpi,
                    win.Bounds.Top,
                    occluders);

                int segIdx = 0;
                double minSpan = MinWalkableSpanLogical * dpi;
                foreach (var (segStart, segEnd) in visibleTopSegments)
                {
                    if (segEnd - segStart < minSpan)
                    {
                        continue;
                    }

                    surfaces.Add(new WalkableSurface(
                        $"win:{win.Hwnd.ToInt64()}:top:{segIdx++}",
                        win.Hwnd,
                        SurfaceKind.WindowTop,
                        segStart,
                        segEnd,
                        win.Bounds.Top,
                        deltaX,
                        deltaY,
                        dpi,
                        win.ZOrder,
                        win.Title));
                }
            }

            // Extract unoccluded vertical climbable edges (left and right sides of windows).
            double bottomCap = monitor is not null
                ? Math.Min(win.Bounds.Bottom, monitor.WorkArea.Bottom)
                : win.Bounds.Bottom;

            if (bottomCap - win.Bounds.Top >= MinClimbableSpanLogical * dpi)
            {
                ExtractClimbableSide(
                    walls,
                    win,
                    WallSide.Left,
                    win.Bounds.Left,
                    win.Bounds.Top,
                    bottomCap,
                    deltaX,
                    deltaY,
                    dpi,
                    monitor,
                    occluders);

                ExtractClimbableSide(
                    walls,
                    win,
                    WallSide.Right,
                    win.Bounds.Right,
                    win.Bounds.Top,
                    bottomCap,
                    deltaX,
                    deltaY,
                    dpi,
                    monitor,
                    occluders);
            }

            occluders.Add(win.Bounds);
        }

        PixelRect virtualBounds = ComputeVirtualBounds(monitors);

        return new DesktopEnvironmentSnapshot(
            sequenceId,
            DateTime.UtcNow,
            monitors,
            taskbars,
            orderedWindows,
            surfaces,
            walls,
            virtualBounds,
            isExclusiveFullscreen);
    }

    private static void ExtractClimbableSide(
        List<ClimbableWall> walls,
        TrackedWindow win,
        WallSide side,
        double x,
        double topY,
        double bottomY,
        double deltaX,
        double deltaY,
        double dpi,
        MonitorInfoData? monitor,
        IReadOnlyList<PixelRect> occluders)
    {
        if (monitor is not null)
        {
            double edgeMargin = 20.0 * dpi;
            if (side == WallSide.Left && x < monitor.Bounds.Left + edgeMargin)
            {
                return;
            }

            if (side == WallSide.Right && x > monitor.Bounds.Right - edgeMargin)
            {
                return;
            }
        }

        var visibleVertical = ClipVerticalSegment(topY, bottomY, x, occluders);
        double minClimb = MinClimbableSpanLogical * dpi;
        int idx = 0;
        foreach (var (segTop, segBottom) in visibleVertical)
        {
            if (segBottom - segTop < minClimb)
            {
                continue;
            }

            walls.Add(new ClimbableWall(
                $"wall:{win.Hwnd.ToInt64()}:{side}:{idx++}",
                win.Hwnd,
                side,
                x,
                segTop,
                segBottom,
                deltaX,
                deltaY,
                dpi,
                win.ZOrder));
        }
    }

    /// <summary>
    /// Subtracts occluding higher-Z window rectangles from a horizontal segment at height y.
    /// </summary>
    public static IReadOnlyList<(double Start, double End)> ClipHorizontalSegment(
        double start,
        double end,
        double y,
        IReadOnlyList<PixelRect> occluders)
    {
        if (end <= start)
        {
            return Array.Empty<(double, double)>();
        }

        var segments = new List<(double Start, double End)> { (start, end) };

        foreach (var occ in occluders)
        {
            // An upper window hides the top edge at y if its vertical range covers y (including titlebar area).
            if (occ.Top > y + 2 || occ.Bottom <= y)
            {
                continue;
            }

            var next = new List<(double Start, double End)>(segments.Count);
            foreach (var seg in segments)
            {
                if (occ.Right <= seg.Start || occ.Left >= seg.End)
                {
                    next.Add(seg);
                    continue;
                }

                if (occ.Left > seg.Start)
                {
                    next.Add((seg.Start, Math.Min(seg.End, occ.Left)));
                }

                if (occ.Right < seg.End)
                {
                    next.Add((Math.Max(seg.Start, occ.Right), seg.End));
                }
            }

            segments = next;
            if (segments.Count == 0)
            {
                break;
            }
        }

        return segments;
    }

    /// <summary>
    /// Subtracts occluding higher-Z window rectangles from a vertical segment at coordinate x.
    /// </summary>
    public static IReadOnlyList<(double Top, double Bottom)> ClipVerticalSegment(
        double top,
        double bottom,
        double x,
        IReadOnlyList<PixelRect> occluders)
    {
        if (bottom <= top)
        {
            return Array.Empty<(double, double)>();
        }

        var segments = new List<(double Top, double Bottom)> { (top, bottom) };

        foreach (var occ in occluders)
        {
            if (occ.Left > x + 2 || occ.Right < x - 2)
            {
                continue;
            }

            var next = new List<(double Top, double Bottom)>(segments.Count);
            foreach (var seg in segments)
            {
                if (occ.Bottom <= seg.Top || occ.Top >= seg.Bottom)
                {
                    next.Add(seg);
                    continue;
                }

                if (occ.Top > seg.Top)
                {
                    next.Add((seg.Top, Math.Min(seg.Bottom, occ.Top)));
                }

                if (occ.Bottom < seg.Bottom)
                {
                    next.Add((Math.Max(seg.Top, occ.Bottom), seg.Bottom));
                }
            }

            segments = next;
            if (segments.Count == 0)
            {
                break;
            }
        }

        return segments;
    }

    private static MonitorInfoData? FindContainingMonitor(IReadOnlyList<MonitorInfoData> monitors, PixelRect rect)
    {
        double cx = (rect.Left + rect.Right) * 0.5;
        double cy = (rect.Top + rect.Bottom) * 0.5;
        foreach (var monitor in monitors)
        {
            if (monitor.Bounds.Contains(cx, cy))
            {
                return monitor;
            }
        }

        return monitors.FirstOrDefault(m => m.Bounds.Intersects(rect)) ?? monitors.FirstOrDefault();
    }

    private static PixelRect ComputeVirtualBounds(IReadOnlyList<MonitorInfoData> monitors)
    {
        if (monitors.Count == 0)
        {
            return new PixelRect(0, 0, 1920, 1080);
        }

        double left = monitors.Min(m => m.Bounds.Left);
        double top = monitors.Min(m => m.Bounds.Top);
        double right = monitors.Max(m => m.Bounds.Right);
        double bottom = monitors.Max(m => m.Bounds.Bottom);
        return new PixelRect(left, top, right, bottom);
    }
}
