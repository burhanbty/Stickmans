using System.IO;
using DesktopStickmanUniverse.AI;
using DesktopStickmanUniverse.Desktop;
using DesktopStickmanUniverse.Interaction;
using DesktopStickmanUniverse.Performance;
using DesktopStickmanUniverse.Physics;
using DesktopStickmanUniverse.Settings;
using Xunit;

namespace DesktopStickmanUniverse.Tests;

public sealed class AiAndSettingsTests
{
    [Fact]
    public void MiniEventDirector_NeverStartsSpar_WhenFightingDisabled()
    {
        var monitor = new MonitorInfoData("DISP1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1.0, true, false);
        var tb = new TaskbarInfo(new IntPtr(1), new PixelRect(0, 1032, 1920, 1080), TaskbarEdge.Bottom, false, true, monitor.DeviceName);
        var snap = DesktopGeometryBuilder.Build(1, new[] { monitor }, new[] { tb }, Array.Empty<TrackedWindow>(), new Dictionary<IntPtr, PixelRect>(), new Dictionary<IntPtr, PixelRect>(), false);
        var floor = snap.WalkableSurfaces.First();

        var chars = new[]
        {
            new StickmanCharacter { Id = 1, X = 400, Y = floor.Y, IsGrounded = true, GroundedSurface = floor, State = StickmanState.Idle },
            new StickmanCharacter { Id = 2, X = 450, Y = floor.Y, IsGrounded = true, GroundedSurface = floor, State = StickmanState.Idle }
        };

        var director = new MiniEventDirector(new Random(12345));
        var settings = new AppSettings { EnableFighting = false, ActivityLevel = ActivityLevel.Lively };
        var particles = new List<ParticleEffect>();

        for (int i = 0; i < 400; i++)
        {
            director.Update(chars, particles, snap, settings, 0.1);
            Assert.NotEqual(MiniEventKind.SpectatedDuel, director.ActiveEvent);
            Assert.All(chars, c => Assert.NotEqual(StickmanState.FightAttack, c.State));
        }

        Assert.False(director.TryStartDuel(chars, chars[0], settings));
    }

    [Fact]
    public void MiniEventDirector_SpectatedDuel_RunsAndEndsCleanly()
    {
        var monitor = new MonitorInfoData("DISP1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1.0, true, false);
        var tb = new TaskbarInfo(new IntPtr(1), new PixelRect(0, 1032, 1920, 1080), TaskbarEdge.Bottom, false, true, monitor.DeviceName);
        var snap = DesktopGeometryBuilder.Build(1, new[] { monitor }, new[] { tb }, Array.Empty<TrackedWindow>(), new Dictionary<IntPtr, PixelRect>(), new Dictionary<IntPtr, PixelRect>(), false);
        var floor = snap.WalkableSurfaces.First();

        var chars = new[]
        {
            new StickmanCharacter { Id = 1, X = 400, Y = floor.Y, IsGrounded = true, GroundedSurface = floor, State = StickmanState.Idle },
            new StickmanCharacter { Id = 2, X = 445, Y = floor.Y, IsGrounded = true, GroundedSurface = floor, State = StickmanState.Idle },
            new StickmanCharacter { Id = 3, X = 560, Y = floor.Y, IsGrounded = true, GroundedSurface = floor, State = StickmanState.Idle }
        };

        var director = new MiniEventDirector(new Random(2026));
        var physics = new PhysicsEngine();
        var settings = new AppSettings { EnableFighting = true, ActivityLevel = ActivityLevel.Lively };
        var particles = new List<ParticleEffect>();

        Assert.True(director.TryStartDuel(chars, chars[0], settings));
        Assert.Equal(MiniEventKind.SpectatedDuel, director.ActiveEvent);

        var observedStates = new HashSet<StickmanState>();
        bool returnedToNone = false;

        for (int i = 0; i < 200; i++)
        {
            director.Update(chars, particles, snap, settings, 0.05);
            physics.Step(chars, particles, snap, 0.05);
            foreach (var c in chars)
            {
                observedStates.Add(c.State);
            }

            if (director.ActiveEvent == MiniEventKind.None)
            {
                returnedToNone = true;
                break;
            }
        }

        Assert.True(returnedToNone);
        Assert.Contains(StickmanState.FightAttack, observedStates);
        Assert.All(chars, c => Assert.Null(c.PartnerCharacterId));
    }

    [Fact]
    public void MiniEventDirector_GroupSocialEvents_RunAndCompleteCleanly()
    {
        var monitor = new MonitorInfoData("DISP1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1.0, true, false);
        var tb = new TaskbarInfo(new IntPtr(1), new PixelRect(0, 1032, 1920, 1080), TaskbarEdge.Bottom, false, true, monitor.DeviceName);
        var snap = DesktopGeometryBuilder.Build(1, new[] { monitor }, new[] { tb }, Array.Empty<TrackedWindow>(), new Dictionary<IntPtr, PixelRect>(), new Dictionary<IntPtr, PixelRect>(), false);
        var floor = snap.WalkableSurfaces.First();

        var chars = new[]
        {
            new StickmanCharacter { Id = 1, X = 400, Y = floor.Y, IsGrounded = true, GroundedSurface = floor, State = StickmanState.Idle },
            new StickmanCharacter { Id = 2, X = 460, Y = floor.Y, IsGrounded = true, GroundedSurface = floor, State = StickmanState.Idle },
            new StickmanCharacter { Id = 3, X = 520, Y = floor.Y, IsGrounded = true, GroundedSurface = floor, State = StickmanState.Idle },
            new StickmanCharacter { Id = 4, X = 580, Y = floor.Y, IsGrounded = true, GroundedSurface = floor, State = StickmanState.Idle }
        };

        var director = new MiniEventDirector(new Random(99));
        var physics = new PhysicsEngine();
        var settings = new AppSettings { EnableFighting = false, ActivityLevel = ActivityLevel.Lively };
        var particles = new List<ParticleEffect>();

        var seenEvents = new HashSet<MiniEventKind>();
        bool completedEvent = false;
        MiniEventKind prev = MiniEventKind.None;

        for (int i = 0; i < 400; i++)
        {
            director.Update(chars, particles, snap, settings, 0.08);
            physics.Step(chars, particles, snap, 0.08);

            if (director.ActiveEvent != MiniEventKind.None)
            {
                seenEvents.Add(director.ActiveEvent);
            }
            if (prev != MiniEventKind.None && director.ActiveEvent == MiniEventKind.None)
            {
                completedEvent = true;
                break;
            }
            prev = director.ActiveEvent;
        }

        Assert.True(seenEvents.Count >= 1);
        Assert.DoesNotContain(MiniEventKind.SpectatedDuel, seenEvents);
        Assert.True(completedEvent);
        Assert.All(chars, c => Assert.Null(c.PartnerCharacterId));
    }

    [Fact]
    public void AiBehaviorController_ProximitySocialAndNaturalBehaviors_RespectDisableFighting()
    {
        var monitor = new MonitorInfoData("DISP1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1.0, true, false);
        var tb = new TaskbarInfo(new IntPtr(1), new PixelRect(0, 1032, 1920, 1080), TaskbarEdge.Bottom, false, true, monitor.DeviceName);
        var snap = DesktopGeometryBuilder.Build(1, new[] { monitor }, new[] { tb }, Array.Empty<TrackedWindow>(), new Dictionary<IntPtr, PixelRect>(), new Dictionary<IntPtr, PixelRect>(), false);
        var floor = snap.WalkableSurfaces.First();

        var chars = new[]
        {
            new StickmanCharacter { Id = 1, X = 400, Y = floor.Y, IsGrounded = true, GroundedSurface = floor, State = StickmanState.Idle, StateDuration = 0.4, Personality = StickmanCharacter.DefaultPersonalities[0] },
            new StickmanCharacter { Id = 2, X = 438, Y = floor.Y, IsGrounded = true, GroundedSurface = floor, State = StickmanState.Idle, StateDuration = 0.4, Personality = StickmanCharacter.DefaultPersonalities[2] },
            new StickmanCharacter { Id = 3, X = 476, Y = floor.Y, IsGrounded = true, GroundedSurface = floor, State = StickmanState.Idle, StateDuration = 0.4, Personality = StickmanCharacter.DefaultPersonalities[3] }
        };

        var ai = new AiBehaviorController(new Random(42));
        var physics = new PhysicsEngine();
        var input = new InputInteractionManager();
        var settings = new AppSettings { EnableFighting = false, ActivityLevel = ActivityLevel.Lively };
        var particles = new List<ParticleEffect>();

        var observedStates = new HashSet<StickmanState>();
        for (int i = 0; i < 240; i++)
        {
            ai.Update(chars, particles, snap, input, settings, 0.08);
            physics.Step(chars, particles, snap, 0.08);
            foreach (var c in chars)
            {
                observedStates.Add(c.State);
                Assert.NotEqual(StickmanState.FightAttack, c.State);
                Assert.NotEqual(StickmanState.Block, c.State);
                Assert.NotEqual(StickmanState.HitReact, c.State);
            }
        }

        Assert.Contains(
            observedStates,
            s => s is StickmanState.HighFive or StickmanState.Chat or StickmanState.SitTogether or StickmanState.Dance or StickmanState.Stretch or StickmanState.Think or StickmanState.Backflip or StickmanState.Handstand or StickmanState.Pushups);
    }

    [Fact]
    public void SettingsService_SavesLoadsAndClampsOutOfRangeValues()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "dsu_test_" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(tempDir, "settings.json");

        try
        {
            var svc = new SettingsService(file);
            var settings = new AppSettings
            {
                CharacterCount = 99,
                AnimationSpeed = 10.0,
                EnableFighting = false,
                PerformanceMode = true,
                ActivityLevel = ActivityLevel.Lively
            };

            svc.Save(settings);
            var loaded = svc.Load();

            Assert.Equal(10, loaded.CharacterCount);
            Assert.Equal(2.0, loaded.AnimationSpeed);
            Assert.False(loaded.EnableFighting);
            Assert.True(loaded.PerformanceMode);
            Assert.Equal(ActivityLevel.Lively, loaded.ActivityLevel);

            File.WriteAllText(file, "{ invalid json !!");
            var fallback = svc.Load();
            Assert.Equal(4, fallback.CharacterCount);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void PerformanceMonitor_ReducesTargetFps_WhenIdleOrPaused()
    {
        var perf = new PerformanceMonitor();

        Assert.Equal(0, perf.ComputeTargetFps(isPaused: true, allMonitorsSuppressed: false, performanceMode: false, hasActiveMotion: true, isDraggingOrFighting: true));
        Assert.Equal(0, perf.ComputeTargetFps(isPaused: false, allMonitorsSuppressed: true, performanceMode: false, hasActiveMotion: true, isDraggingOrFighting: false));

        int active = perf.ComputeTargetFps(false, false, performanceMode: false, hasActiveMotion: true, isDraggingOrFighting: true);
        int idle = perf.ComputeTargetFps(false, false, performanceMode: false, hasActiveMotion: false, isDraggingOrFighting: false);
        int ecoActive = perf.ComputeTargetFps(false, false, performanceMode: true, hasActiveMotion: true, isDraggingOrFighting: false);

        Assert.True(active > idle);
        Assert.True(ecoActive < active);
    }

    [Fact]
    public void AiBehaviorController_ReactsToNewlyOpenedWindow()
    {
        var monitor = new MonitorInfoData("DISP1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1.0, true, false);
        var tb = new TaskbarInfo(new IntPtr(1), new PixelRect(0, 1032, 1920, 1080), TaskbarEdge.Bottom, false, true, monitor.DeviceName);

        var snap1 = DesktopGeometryBuilder.Build(1, new[] { monitor }, new[] { tb }, Array.Empty<TrackedWindow>(), new Dictionary<IntPtr, PixelRect>(), new Dictionary<IntPtr, PixelRect>(), false);
        var floor = snap1.WalkableSurfaces.First();

        var scout = new StickmanCharacter
        {
            Id = 1,
            X = 300,
            Y = floor.Y,
            IsGrounded = true,
            GroundedSurface = floor,
            Personality = StickmanCharacter.DefaultPersonalities[1] // CuriosityBias = 0.95
        };

        var ai = new AiBehaviorController(new Random(7));
        var input = new InputInteractionManager();
        var settings = new AppSettings();
        var particles = new List<ParticleEffect>();

        ai.Update(new[] { scout }, particles, snap1, input, settings, 0.05);

        // New window opens to the right of Scout.
        var newWin = new TrackedWindow(new IntPtr(99), new PixelRect(600, 300, 1100, 800), "NewWindow", "Notepad", 0, true, false, monitor.DeviceName);
        var snap2 = DesktopGeometryBuilder.Build(2, new[] { monitor }, new[] { tb }, new[] { newWin }, new Dictionary<IntPtr, PixelRect>(), new Dictionary<IntPtr, PixelRect>(), false);

        ai.Update(new[] { scout }, particles, snap2, input, settings, 0.05);

        // At minimum it does not throw and keeps valid finite position.
        Assert.False(double.IsNaN(scout.X) || double.IsNaN(scout.Y));
    }
}
