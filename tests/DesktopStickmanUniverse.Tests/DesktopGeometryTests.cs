using DesktopStickmanUniverse.Desktop;
using Xunit;

namespace DesktopStickmanUniverse.Tests;

public sealed class DesktopGeometryTests
{
    private static readonly MonitorInfoData PrimaryMonitor = new(
        @"\\.\DISPLAY1",
        new PixelRect(0, 0, 1920, 1080),
        new PixelRect(0, 0, 1920, 1032),
        1.0,
        true,
        false);

    [Fact]
    public void Build_ClipsLowerWindowTopEdge_WhenHigherWindowOverlapsIt()
    {
        var taskbar = new TaskbarInfo(new IntPtr(1), new PixelRect(0, 1032, 1920, 1080), TaskbarEdge.Bottom, false, true, PrimaryMonitor.DeviceName);

        // Front window (ZOrder = 0) covers middle portion of Back window (ZOrder = 1) top edge.
        var frontWin = new TrackedWindow(new IntPtr(10), new PixelRect(400, 150, 700, 500), "Front", "Chrome", 0, true, false, PrimaryMonitor.DeviceName);
        var backWin = new TrackedWindow(new IntPtr(20), new PixelRect(200, 220, 900, 620), "Back", "Notepad", 1, false, false, PrimaryMonitor.DeviceName);

        var snapshot = DesktopGeometryBuilder.Build(
            1,
            new[] { PrimaryMonitor },
            new[] { taskbar },
            new[] { frontWin, backWin },
            new Dictionary<IntPtr, PixelRect>(),
            new Dictionary<IntPtr, PixelRect>(),
            false);

        var backTopSegments = snapshot.WalkableSurfaces
            .Where(s => s.SourceHwnd == backWin.Hwnd && s.Kind == SurfaceKind.WindowTop)
            .OrderBy(s => s.Left)
            .ToList();

        Assert.Equal(2, backTopSegments.Count);
        Assert.True(backTopSegments[0].Right <= 400.5);
        Assert.True(backTopSegments[1].Left >= 699.5);
    }

    [Fact]
    public void Build_CompletelyOccludedWindowTop_ProducesNoWalkableSegments()
    {
        var front = new TrackedWindow(new IntPtr(10), new PixelRect(100, 100, 1000, 700), "Front", "Code", 0, true, false, PrimaryMonitor.DeviceName);
        var back = new TrackedWindow(new IntPtr(20), new PixelRect(200, 200, 800, 600), "Back", "Explorer", 1, false, false, PrimaryMonitor.DeviceName);

        var snapshot = DesktopGeometryBuilder.Build(
            1,
            new[] { PrimaryMonitor },
            Array.Empty<TaskbarInfo>(),
            new[] { front, back },
            new Dictionary<IntPtr, PixelRect>(),
            new Dictionary<IntPtr, PixelRect>(),
            false);

        Assert.DoesNotContain(snapshot.WalkableSurfaces, s => s.SourceHwnd == back.Hwnd);
    }

    [Fact]
    public void Build_ComputesMovementDelta_FromPreviousBounds()
    {
        var prev = new Dictionary<IntPtr, PixelRect>
        {
            [new IntPtr(42)] = new PixelRect(300, 200, 800, 600)
        };

        var movedWin = new TrackedWindow(new IntPtr(42), new PixelRect(345, 220, 845, 620), "Moved", "Notepad", 0, true, false, PrimaryMonitor.DeviceName);

        var snapshot = DesktopGeometryBuilder.Build(
            2,
            new[] { PrimaryMonitor },
            Array.Empty<TaskbarInfo>(),
            new[] { movedWin },
            prev,
            new Dictionary<IntPtr, PixelRect>(),
            false);

        var surface = snapshot.WalkableSurfaces.Single(s => s.SourceHwnd == movedWin.Hwnd);
        Assert.Equal(45.0, surface.DeltaX);
        Assert.Equal(20.0, surface.DeltaY);
    }

    [Fact]
    public void Build_WindowNearMonitorTop_SkipsWalkableTopButStillOccludesLowerWindows()
    {
        var maxWin = new TrackedWindow(new IntPtr(100), new PixelRect(0, 0, 1920, 1032), "MaxChrome", "Chrome", 0, true, true, PrimaryMonitor.DeviceName);
        var hiddenWin = new TrackedWindow(new IntPtr(101), new PixelRect(200, 180, 900, 600), "Behind", "Notepad", 1, false, false, PrimaryMonitor.DeviceName);

        var snapshot = DesktopGeometryBuilder.Build(
            1,
            new[] { PrimaryMonitor },
            Array.Empty<TaskbarInfo>(),
            new[] { maxWin, hiddenWin },
            new Dictionary<IntPtr, PixelRect>(),
            new Dictionary<IntPtr, PixelRect>(),
            false);

        Assert.DoesNotContain(snapshot.WalkableSurfaces, s => s.SourceHwnd == maxWin.Hwnd);
        Assert.DoesNotContain(snapshot.WalkableSurfaces, s => s.SourceHwnd == hiddenWin.Hwnd);
    }

    [Fact]
    public void Build_HiddenAutoHideTaskbar_UsesMonitorBottomAsFloor()
    {
        var fullWorkMonitor = new MonitorInfoData(
            @"\\.\DISPLAY1",
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080),
            1.0,
            true,
            false);

        var hiddenTaskbar = new TaskbarInfo(
            new IntPtr(5),
            new PixelRect(0, 1078, 1920, 1080),
            TaskbarEdge.Bottom,
            IsAutoHide: true,
            IsVisible: false,
            fullWorkMonitor.DeviceName);

        var snapshot = DesktopGeometryBuilder.Build(
            1,
            new[] { fullWorkMonitor },
            new[] { hiddenTaskbar },
            Array.Empty<TrackedWindow>(),
            new Dictionary<IntPtr, PixelRect>(),
            new Dictionary<IntPtr, PixelRect>(),
            false);

        var floor = snapshot.WalkableSurfaces.Single(s => s.Kind == SurfaceKind.MonitorFloor);
        Assert.Equal(1080.0, floor.Y);
    }
}
