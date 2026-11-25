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
    public void MiniEventDirector_ManualModeStopsAndClearsEventTargets()
    {
        var floor = new WalkableSurface("floor", IntPtr.Zero, SurfaceKind.MonitorFloor, 0, 1200, 720, 0, 0, 1, 0, "Floor");
        var snap = DesktopEnvironmentSnapshot.Empty with { WalkableSurfaces = new[] { floor } };
        var characters = new[]
        {
            new StickmanCharacter { Id = 1, X = 400, Y = floor.Y, IsGrounded = true, GroundedSurface = floor },
            new StickmanCharacter { Id = 2, X = 460, Y = floor.Y, IsGrounded = true, GroundedSurface = floor }
        };
        var settings = new AppSettings { EnableAutomaticMiniEvents = false, ActivityLevel = ActivityLevel.Lively };
        var director = new MiniEventDirector();

        director.Update(characters, new List<ParticleEffect>(), snap, settings, 10);
        Assert.Equal(MiniEventKind.None, director.ActiveEvent);
        Assert.Equal(MiniEventStartResult.Started, director.TryStartEvent(MiniEventKind.DanceParty, characters, snap, settings));
        Assert.True(director.RemainingSeconds > 0);
        Assert.All(characters, c => Assert.NotNull(c.TargetSurface));

        director.StopEvent(characters, settings);
        Assert.Equal(MiniEventKind.None, director.ActiveEvent);
        Assert.Equal(0, director.RemainingSeconds);
        Assert.All(characters, c =>
        {
            Assert.Null(c.PartnerCharacterId);
            Assert.Null(c.MoveTargetX);
            Assert.Null(c.TargetSurface);
        });
    }

    [Fact]
    public void SettingsService_DefaultsOldSettingsAndNormalizesNicknames()
    {
        string path = Path.Combine(Path.GetTempPath(), $"stickmans_test_{Guid.NewGuid():N}.json");
        var service = new SettingsService(path);
        try
        {
            File.WriteAllText(path, "{\"CharacterCount\": 3}");
            var settings = service.Load();
            Assert.True(settings.EnableAutomaticMiniEvents);
            Assert.Equal("Spark", settings.GetNameForIndex(0));

            settings.CharacterNames = new List<string> { "  Kıvılcım  ", " ", new string('A', 23) + "✨🪴", "Dördüncü" };
            settings.EnableAutomaticMiniEvents = false;
            service.Save(settings);

            var loaded = service.Load();
            Assert.False(loaded.EnableAutomaticMiniEvents);
            Assert.Equal("Kıvılcım", loaded.GetNameForIndex(0));
            Assert.Equal("Scout", loaded.GetNameForIndex(1));
            Assert.Equal(AppSettings.MaxCharacterNameLength, loaded.GetNameForIndex(2).Length);
            Assert.Equal("Dördüncü", loaded.GetNameForIndex(3));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(MiniEventKind.DanceParty)]
    [InlineData(MiniEventKind.TagChase)]
    [InlineData(MiniEventKind.AcrobaticsShow)]
    [InlineData(MiniEventKind.GatheringChat)]
    [InlineData(MiniEventKind.SummitClimb)]
    [InlineData(MiniEventKind.SpectatedDuel)]
    public void MiniEventDirector_ManualStartSelectsRequestedEvent(MiniEventKind kind)
    {
        var narrowSurface = new WalkableSurface("ledge", new IntPtr(1), SurfaceKind.WindowTop, 400, 480, 250, 0, 0, 1, 0, "Window");
        var snapshot = DesktopEnvironmentSnapshot.Empty with { WalkableSurfaces = new[] { narrowSurface } };
        var characters = new[]
        {
            new StickmanCharacter { Id = 1, X = 420, Y = narrowSurface.Y, IsGrounded = true, GroundedSurface = narrowSurface, MiniEventCooldown = 3 },
            new StickmanCharacter { Id = 2, X = 460, Y = narrowSurface.Y, IsGrounded = true, GroundedSurface = narrowSurface, MiniEventCooldown = 3 }
        };
        var director = new MiniEventDirector(new Random(14));
        var settings = new AppSettings { EnableFighting = true };

        Assert.Equal(MiniEventStartResult.Started, director.TryStartEvent(kind, characters, snapshot, settings));
        Assert.Equal(kind, director.ActiveEvent);
        Assert.Equal(MiniEventStartResult.EventInProgress,
            director.TryStartEvent(MiniEventKind.TagChase, characters, snapshot, settings));
        Assert.Equal(kind, director.ActiveEvent);
    }

    [Fact]
    public void MiniEventDirector_ManualStartReportsUnmetRequirements()
    {
        var floor = new WalkableSurface("floor", IntPtr.Zero, SurfaceKind.MonitorFloor, 0, 1200, 720, 0, 0, 1, 0, "Floor");
        var snapshot = DesktopEnvironmentSnapshot.Empty with { WalkableSurfaces = new[] { floor } };
        var characters = new[]
        {
            new StickmanCharacter { Id = 1, X = 400, Y = floor.Y, IsGrounded = true, GroundedSurface = floor },
            new StickmanCharacter { Id = 2, X = 460, Y = floor.Y, IsGrounded = true, GroundedSurface = floor }
        };
        var director = new MiniEventDirector();
        var settings = new AppSettings { EnableFighting = false };

        Assert.Equal(MiniEventStartResult.FightingDisabled,
            director.TryStartEvent(MiniEventKind.SpectatedDuel, characters, snapshot, settings));
        Assert.Equal(MiniEventStartResult.NoSuitableWindow,
            director.TryStartEvent(MiniEventKind.SummitClimb, characters, snapshot, settings));

        characters[1].IsGrabbed = true;
        Assert.Equal(MiniEventStartResult.TooFewAvailableCharacters,
            director.TryStartEvent(MiniEventKind.DanceParty, characters, snapshot, settings));
        Assert.Equal(MiniEventKind.None, director.ActiveEvent);
        Assert.All(characters, character => Assert.Null(character.PartnerCharacterId));
    }

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
        var settings = new AppSettings { EnableAutomaticMiniEvents = false };
        var particles = new List<ParticleEffect>();

        ai.Update(new[] { scout }, particles, snap1, input, settings, 0.05);

        // New window opens to the right of Scout.
        var newWin = new TrackedWindow(new IntPtr(99), new PixelRect(600, 300, 1100, 800), "NewWindow", "Notepad", 0, true, false, monitor.DeviceName);
        var snap2 = DesktopGeometryBuilder.Build(2, new[] { monitor }, new[] { tb }, new[] { newWin }, new Dictionary<IntPtr, PixelRect>(), new Dictionary<IntPtr, PixelRect>(), false);

        ai.Update(new[] { scout }, particles, snap2, input, settings, 0.05);

        Assert.Null(scout.EmoteText);
        ai.Update(new[] { scout }, particles, snap2, input, settings, 0.35);
        Assert.Equal(StickmanState.Wave, scout.State);
        Assert.Equal("Hi!", scout.EmoteText);
        Assert.Equal(1, scout.Facing);
    }

    [Theory]
    [InlineData(52, 0, "Document", StickmanState.Brace, "Whoa!")]
    [InlineData(0, 90, "Document", StickmanState.Think, "Hmm?")]
    [InlineData(0, 0, "Another document", StickmanState.Peek, "?")]
    public void AiBehaviorController_RespondsToSettledActiveWindowChanges(
        int offset, int addedWidth, string caption, StickmanState expectedState, string expectedEmote)
    {
        var monitor = new MonitorInfoData("Display", new PixelRect(0, 0, 1200, 900), new PixelRect(0, 0, 1200, 860), 1, true, false);
        var window = new TrackedWindow(new IntPtr(99), new PixelRect(200, 300, 800, 660), "Document", "Editor", 0, true, false, monitor.DeviceName);
        var ledge = new WalkableSurface("ledge", window.Hwnd, SurfaceKind.WindowTop, 200, 800, 300, 0, 0, 1, 0, string.Empty);
        var first = DesktopEnvironmentSnapshot.Empty with { SequenceId = 1, Windows = new[] { window }, Monitors = new[] { monitor } };
        var scout = new StickmanCharacter { Id = 1, X = 410, Y = ledge.Y, IsGrounded = true, GroundedSurface = ledge, StateDuration = 20 };
        var ai = new AiBehaviorController(new Random(7));
        var input = new InputInteractionManager();
        var settings = new AppSettings { EnableAutomaticMiniEvents = false };
        var particles = new List<ParticleEffect>();
        ai.Update(new[] { scout }, particles, first, input, settings, 0.05);
        Assert.Null(scout.EmoteText);

        var changedWindow = window with
        {
            Bounds = window.Bounds with { Left = window.Bounds.Left + offset, Right = window.Bounds.Right + offset + addedWidth },
            Title = caption
        };
        var changed = first with { SequenceId = 2, Windows = new[] { changedWindow } };
        ai.Update(new[] { scout }, particles, changed, input, settings, 0.15);
        Assert.Null(scout.EmoteText);

        ai.Update(new[] { scout }, particles, changed, input, settings, 0.55);
        Assert.Equal(expectedState, scout.State);
        Assert.Equal(expectedEmote, scout.EmoteText);

        // Repeated frames with the same snapshot do not retrigger the reaction.
        ai.Update(new[] { scout }, particles, changed, input, settings, 0.1);
        Assert.InRange(scout.EmoteTimer, 1.09, 1.11);
    }

    [Fact]
    public void AiBehaviorController_IgnoresBackgroundChangesAndRebaselinesAfterDisable()
    {
        var monitor = new MonitorInfoData("Display", new PixelRect(0, 0, 1200, 900), new PixelRect(0, 0, 1200, 860), 1, true, false);
        var editor = new TrackedWindow(new IntPtr(1), new PixelRect(180, 250, 920, 700), "Document", "Editor", 0, true, false, monitor.DeviceName);
        var background = editor with { Hwnd = new IntPtr(2), Title = "Other app", IsForeground = false };
        var snap = DesktopEnvironmentSnapshot.Empty with { SequenceId = 1, Windows = new[] { editor, background }, Monitors = new[] { monitor } };
        var floor = new WalkableSurface("floor", IntPtr.Zero, SurfaceKind.MonitorFloor, 0, 1200, 860, 0, 0, 1, 0, string.Empty);
        var scout = new StickmanCharacter { Id = 1, X = 410, Y = floor.Y, IsGrounded = true, GroundedSurface = floor, StateDuration = 20 };
        var ai = new AiBehaviorController();
        var input = new InputInteractionManager();
        var settings = new AppSettings { EnableAutomaticMiniEvents = false };
        var particles = new List<ParticleEffect>();

        ai.Update(new[] { scout }, particles, snap, input, settings, 0.05);
        var backgroundChange = snap with { SequenceId = 2, Windows = new[] { editor, background with { Title = "Changed" } } };
        ai.Update(new[] { scout }, particles, backgroundChange, input, settings, 0.8);
        Assert.Null(scout.EmoteText);

        settings.EnableWorkspaceReactions = false;
        var activeChange = snap with { SequenceId = 3, Windows = new[] { editor with { Title = "New document" } } };
        ai.Update(new[] { scout }, particles, activeChange, input, settings, 0.8);
        settings.EnableWorkspaceReactions = true;
        ai.Update(new[] { scout }, particles, activeChange, input, settings, 0.8);
        Assert.Null(scout.EmoteText);
    }
}
