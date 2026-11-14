using DesktopStickmanUniverse.Desktop;
using DesktopStickmanUniverse.Physics;
using Xunit;

namespace DesktopStickmanUniverse.Tests;

public sealed class PhysicsEngineTests
{
    private static MonitorInfoData DefaultMonitor => new(
        @"\\.\DISPLAY1",
        new PixelRect(0, 0, 1920, 1080),
        new PixelRect(0, 0, 1920, 1032),
        1.0,
        true,
        false);

    [Fact]
    public void Step_SweptLanding_CatchesFastFallingStickmanOnWindowTop()
    {
        var engine = new PhysicsEngine();
        var win = new TrackedWindow(new IntPtr(11), new PixelRect(200, 400, 800, 800), "App", "Notepad", 0, true, false, DefaultMonitor.DeviceName);
        var tb = new TaskbarInfo(new IntPtr(1), new PixelRect(0, 1032, 1920, 1080), TaskbarEdge.Bottom, false, true, DefaultMonitor.DeviceName);

        var snap = DesktopGeometryBuilder.Build(1, new[] { DefaultMonitor }, new[] { tb }, new[] { win }, new Dictionary<IntPtr, PixelRect>(), new Dictionary<IntPtr, PixelRect>(), false);

        var c = new StickmanCharacter
        {
            Id = 1,
            X = 500,
            Y = 390,
            Vy = 950,
            FallStartY = 200,
            State = StickmanState.Fall,
            IsGrounded = false
        };

        var particles = new List<ParticleEffect>();
        engine.Step(new[] { c }, particles, snap, 0.04);

        Assert.True(c.IsGrounded);
        Assert.Equal(400.0, c.Y);
        Assert.Equal(win.Hwnd, c.GroundedSurface?.SourceHwnd);
    }

    [Fact]
    public void Step_AppliesMovingWindowDeltaOncePerSnapshotSequence()
    {
        var engine = new PhysicsEngine();
        var tb = new TaskbarInfo(new IntPtr(1), new PixelRect(0, 1032, 1920, 1080), TaskbarEdge.Bottom, false, true, DefaultMonitor.DeviceName);
        var win1 = new TrackedWindow(new IntPtr(22), new PixelRect(300, 300, 700, 700), "Win", "Code", 0, true, false, DefaultMonitor.DeviceName);

        var snap1 = DesktopGeometryBuilder.Build(1, new[] { DefaultMonitor }, new[] { tb }, new[] { win1 }, new Dictionary<IntPtr, PixelRect>(), new Dictionary<IntPtr, PixelRect>(), false);
        var top1 = snap1.WalkableSurfaces.Single(s => s.SourceHwnd == win1.Hwnd);

        var c = new StickmanCharacter
        {
            Id = 1,
            X = 450,
            Y = 300,
            IsGrounded = true,
            GroundedSurface = top1,
            State = StickmanState.Idle
        };

        var particles = new List<ParticleEffect>();
        engine.Step(new[] { c }, particles, snap1, 0.016);
        Assert.Equal(450.0, c.X, 2);

        // Move window +35px right, +10px down.
        var prev = new Dictionary<IntPtr, PixelRect> { [win1.Hwnd] = win1.Bounds };
        var win2 = win1 with { Bounds = new PixelRect(335, 310, 735, 710) };
        var snap2 = DesktopGeometryBuilder.Build(2, new[] { DefaultMonitor }, new[] { tb }, new[] { win2 }, prev, new Dictionary<IntPtr, PixelRect>(), false);

        // Step multiple frames with the SAME snapshot sequence ID; delta must apply ONLY once!
        engine.Step(new[] { c }, particles, snap2, 0.016);
        engine.Step(new[] { c }, particles, snap2, 0.016);
        engine.Step(new[] { c }, particles, snap2, 0.016);

        Assert.Equal(485.0, c.X, 1);
        Assert.Equal(310.0, c.Y, 1);
    }

    [Fact]
    public void Step_WhenSupportingWindowDisappears_StickmanDetachesAndFalls()
    {
        var engine = new PhysicsEngine();
        var tb = new TaskbarInfo(new IntPtr(1), new PixelRect(0, 1032, 1920, 1080), TaskbarEdge.Bottom, false, true, DefaultMonitor.DeviceName);
        var win = new TrackedWindow(new IntPtr(33), new PixelRect(250, 350, 650, 750), "Win", "Explorer", 0, true, false, DefaultMonitor.DeviceName);

        var snap1 = DesktopGeometryBuilder.Build(1, new[] { DefaultMonitor }, new[] { tb }, new[] { win }, new Dictionary<IntPtr, PixelRect>(), new Dictionary<IntPtr, PixelRect>(), false);
        var surface = snap1.WalkableSurfaces.Single(s => s.SourceHwnd == win.Hwnd);

        var c = new StickmanCharacter
        {
            Id = 1,
            X = 400,
            Y = 350,
            IsGrounded = true,
            GroundedSurface = surface,
            State = StickmanState.Idle
        };

        var particles = new List<ParticleEffect>();
        engine.Step(new[] { c }, particles, snap1, 0.016);

        // Window minimized -> no longer present in snap2.
        var snap2 = DesktopGeometryBuilder.Build(2, new[] { DefaultMonitor }, new[] { tb }, Array.Empty<TrackedWindow>(), new Dictionary<IntPtr, PixelRect>(), new Dictionary<IntPtr, PixelRect>(), false);
        engine.Step(new[] { c }, particles, snap2, 0.02);

        Assert.False(c.IsGrounded);
        Assert.Equal(StickmanState.Fall, c.State);
        Assert.True(c.Vy > 0);
    }

    [Fact]
    public void Step_ClimbingToTopOfWindow_MantlesOntoWalkableTopEdge()
    {
        var engine = new PhysicsEngine();
        var tb = new TaskbarInfo(new IntPtr(1), new PixelRect(0, 1032, 1920, 1080), TaskbarEdge.Bottom, false, true, DefaultMonitor.DeviceName);
        var win = new TrackedWindow(new IntPtr(44), new PixelRect(300, 300, 800, 700), "Win", "Notepad", 0, true, false, DefaultMonitor.DeviceName);

        var snap = DesktopGeometryBuilder.Build(1, new[] { DefaultMonitor }, new[] { tb }, new[] { win }, new Dictionary<IntPtr, PixelRect>(), new Dictionary<IntPtr, PixelRect>(), false);
        var leftWall = snap.ClimbableWalls.Single(w => w.SourceHwnd == win.Hwnd && w.Side == WallSide.Left);

        var c = new StickmanCharacter
        {
            Id = 1,
            X = leftWall.X,
            Y = leftWall.TopY + 3,
            State = StickmanState.Climb,
            ClimbingWall = leftWall
        };

        var particles = new List<ParticleEffect>();
        engine.Step(new[] { c }, particles, snap, 0.05);

        Assert.True(c.IsGrounded);
        Assert.Equal(300.0, c.Y, 1);
        Assert.Null(c.ClimbingWall);
        Assert.Equal(win.Hwnd, c.GroundedSurface?.SourceHwnd);
    }
}
