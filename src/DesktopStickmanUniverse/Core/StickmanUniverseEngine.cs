using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using DesktopStickmanUniverse.AI;
using DesktopStickmanUniverse.Animation;
using DesktopStickmanUniverse.Desktop;
using DesktopStickmanUniverse.Interaction;
using DesktopStickmanUniverse.Overlay;
using DesktopStickmanUniverse.Performance;
using DesktopStickmanUniverse.Physics;
using DesktopStickmanUniverse.Settings;

namespace DesktopStickmanUniverse.Core;

public enum DesktopPlaybackState
{
    Running,
    Paused,
    Hidden,
    Focusing,
    Unavailable
}

/// <summary>
/// Central coordinator connecting desktop window tracking, physics, AI behaviors,
/// procedural animation, selective input, overlay rendering, and adaptive pacing.
/// </summary>
public sealed class StickmanUniverseEngine : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly SettingsService _settingsService;
    private readonly WindowTracker _windowTracker;
    private readonly OverlayManager _overlayManager;
    private readonly PhysicsEngine _physicsEngine;
    private readonly AiBehaviorController _aiController;
    private readonly InputInteractionManager _inputManager;
    private readonly PerformanceMonitor _performanceMonitor;
    private readonly TimeProvider _timeProvider;

    private readonly List<StickmanCharacter> _characters = new();
    private readonly List<ParticleEffect> _particles = new();
    private readonly DispatcherTimer _tickTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private AppSettings _settings;
    private TimeSpan _lastTickTimestamp;
    private DateTime _lastRenderUtc = DateTime.MinValue;
    private int? _lastHoveredId;
    private DateTimeOffset? _focusEndsAt;
    private bool _disposed;

    public AppSettings Settings => _settings;
    public IReadOnlyList<StickmanCharacter> Characters => _characters;
    public DesktopEnvironmentSnapshot CurrentSnapshot => _windowTracker.CurrentSnapshot;
    public MiniEventKind ActiveMiniEvent => _aiController.CurrentMiniEvent;
    public double MiniEventRemainingSeconds => _aiController.MiniEventRemainingSeconds;
    public bool IsFocusActive => _focusEndsAt.HasValue;
    public TimeSpan FocusTimeRemaining
    {
        get
        {
            TimeSpan remaining = _focusEndsAt.GetValueOrDefault() - _timeProvider.GetUtcNow();
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    private bool ShouldShowCharacters => _settings.CharactersEnabled && !_settings.IsPaused && !IsFocusActive;

    public DesktopPlaybackState PlaybackState
    {
        get
        {
            if (IsFocusActive) return DesktopPlaybackState.Focusing;
            if (!_settings.CharactersEnabled) return DesktopPlaybackState.Hidden;
            if (_settings.IsPaused) return DesktopPlaybackState.Paused;
            return _overlayManager.AreAllMonitorsSuppressed(CurrentSnapshot, true)
                ? DesktopPlaybackState.Unavailable
                : DesktopPlaybackState.Running;
        }
    }
    public PerformanceSnapshot CurrentPerformance => _performanceMonitor.GetSnapshot(
        _windowTracker.LastScanDurationMs,
        _currentTargetFps,
        _overlayManager.ActiveVisibleCount);

    private int _currentTargetFps = 48;

    public event Action? OpenSettingsRequested;
    public event Action<AppSettings>? SettingsChanged;
    public event Action? FocusChanged;

    public StickmanUniverseEngine(Dispatcher dispatcher, SettingsService settingsService, TimeProvider? timeProvider = null)
    {
        _dispatcher = dispatcher;
        _settingsService = settingsService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _settings = _settingsService.Load();

        _windowTracker = new WindowTracker(_dispatcher);
        _overlayManager = new OverlayManager();
        _physicsEngine = new PhysicsEngine();
        _aiController = new AiBehaviorController();
        _inputManager = new InputInteractionManager();
        _performanceMonitor = new PerformanceMonitor();

        _inputManager.AttachOverlay(_overlayManager);
        _inputManager.TryBeginGrabRequested += OnTryBeginGrab;
        _inputManager.TryOpenContextMenuRequested += OnTryOpenContextMenu;
        _inputManager.ChallengeFriendRequested += OnChallengeFriendRequested;
        _inputManager.OpenSettingsRequested += () => OpenSettingsRequested?.Invoke();
        _overlayManager.DisplayEnvironmentChanged += () => _windowTracker.MarkDirty();

        _tickTimer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(20)
        };
        _tickTimer.Tick += OnTick;
    }

    public void Start()
    {
        _windowTracker.Start();
        SyncOverlayVisibility();
        SyncCharacterRoster();
        _lastTickTimestamp = _clock.Elapsed;
        _tickTimer.Start();
    }

    public void ApplySettings(AppSettings updated, bool save = true)
    {
        int prevCount = _settings.CharacterCount;
        var normalized = SettingsService.Normalize(updated.Clone());

        if (save)
        {
            _settingsService.Save(normalized);
        }

        _settings = normalized;
        if (_settings.CharacterCount != prevCount || (!_settings.EnableFighting && ActiveMiniEvent == MiniEventKind.SpectatedDuel))
        {
            StopMiniEvent();
        }

        if (_settings.CharacterCount != prevCount)
        {
            _inputManager.CancelInteraction();
            SyncCharacterRoster();
        }
        else
        {
            UpdateCharacterAppearance();
        }

        SyncOverlayVisibility();
        SettingsChanged?.Invoke(_settings);
    }

    public void StartFocus(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        _focusEndsAt = _timeProvider.GetUtcNow() + duration;
        SyncOverlayVisibility();
        FocusChanged?.Invoke();
    }

    public void EndFocus()
    {
        if (!IsFocusActive)
        {
            return;
        }

        _focusEndsAt = null;
        SyncOverlayVisibility();
        FocusChanged?.Invoke();
    }

    private void SyncOverlayVisibility()
    {
        bool show = ShouldShowCharacters;
        if (!show || !_settings.EnableMouseInteraction)
        {
            _inputManager.CancelInteraction();
            _overlayManager.UpdateClickThrough(null);
        }
        _overlayManager.SyncMonitors(CurrentSnapshot, show);
    }

    public void TogglePause()
    {
        var copy = _settings.Clone();
        copy.IsPaused = !copy.IsPaused;
        ApplySettings(copy);
    }

    public MiniEventStartResult TryStartMiniEvent(MiniEventKind kind)
    {
        var snapshot = _windowTracker.CurrentSnapshot;
        if (_disposed || PlaybackState != DesktopPlaybackState.Running)
        {
            return MiniEventStartResult.DesktopUnavailable;
        }

        return _aiController.TryStartMiniEvent(kind, _characters, snapshot, _settings);
    }

    public void StopMiniEvent() => _aiController.StopMiniEvent(_characters, _settings);

    public void SetCharacterName(int id, string name)
    {
        int index = _characters.FindIndex(character => character.Id == id);
        if (index < 0)
        {
            return;
        }

        var updated = _settings.Clone();
        while (updated.CharacterNames.Count <= index)
        {
            updated.CharacterNames.Add(string.Empty);
        }
        updated.CharacterNames[index] = name;
        ApplySettings(updated);
    }

    public bool TryLocateCharacter(int id)
    {
        var character = _characters.Find(c => c.Id == id);
        if (_disposed || PlaybackState != DesktopPlaybackState.Running || character is null ||
            CurrentSnapshot.FindMonitorAt(character.X, character.Y) is not { HasFullscreenWindow: false })
        {
            return false;
        }

        character.TriggerEmote("Here!", 2.0);
        return true;
    }

    public void ScatterCharacters()
    {
        var snap = _windowTracker.CurrentSnapshot;
        var primary = snap.Monitors.FirstOrDefault(m => m.IsPrimary) ?? snap.Monitors.FirstOrDefault();
        if (primary is null)
        {
            return;
        }

        StopMiniEvent();
        _inputManager.CancelInteraction();
        var rng = new Random();
        for (int i = 0; i < _characters.Count; i++)
        {
            var c = _characters[i];
            c.IsGrabbed = false;
            c.IsGrounded = false;
            c.GroundedSurface = null;
            c.ClimbingWall = null;
            c.FollowCursorMode = false;
            c.X = primary.WorkArea.Left + (primary.WorkArea.Width * (i + 1.0)) / (_characters.Count + 1.0);
            c.Y = primary.WorkArea.Top + 120.0 * primary.DpiScale;
            c.FallStartY = c.Y;
            c.Vx = (rng.NextDouble() - 0.5) * 180.0 * primary.DpiScale;
            c.Vy = -120.0 * primary.DpiScale;
            c.State = StickmanState.Fall;
            c.StateTime = 0;
            c.TriggerEmote("!", 0.8);
        }
    }

    private void SyncCharacterRoster()
    {
        var snap = _windowTracker.CurrentSnapshot;
        var primary = snap.Monitors.FirstOrDefault(m => m.IsPrimary) ?? snap.Monitors.FirstOrDefault();
        double baseX = primary is not null ? primary.WorkArea.Left + primary.WorkArea.Width * 0.5 : 800;
        double floorY = snap.WalkableSurfaces
            .Where(s => s.Kind is SurfaceKind.Taskbar or SurfaceKind.MonitorFloor)
            .Select(s => s.Y)
            .DefaultIfEmpty(primary?.WorkArea.Bottom ?? 980)
            .First();
        double dpi = primary?.DpiScale ?? 1.0;

        int targetCount = Math.Clamp(_settings.CharacterCount, 1, 10);

        while (_characters.Count > targetCount)
        {
            _characters.RemoveAt(_characters.Count - 1);
        }

        while (_characters.Count < targetCount)
        {
            int idx = _characters.Count;
            var profile = StickmanCharacter.DefaultPersonalities[idx % StickmanCharacter.DefaultPersonalities.Count];
            double spread = (idx - (targetCount - 1) * 0.5) * 110.0 * dpi;
            double spawnX = primary is not null
                ? Math.Clamp(baseX + spread, primary.WorkArea.Left + 48 * dpi, primary.WorkArea.Right - 48 * dpi)
                : baseX + spread;

            var c = new StickmanCharacter
            {
                Id = idx + 1,
                Personality = profile,
                X = spawnX,
                Y = floorY - 140.0 * dpi,
                FallStartY = floorY - 140.0 * dpi,
                DpiScale = dpi,
                Facing = idx % 2 == 0 ? 1 : -1,
                State = StickmanState.Fall,
                StateTime = 0,
                StateDuration = 1.0
            };
            _characters.Add(c);
        }

        UpdateCharacterAppearance();
    }

    private void UpdateCharacterAppearance()
    {
        for (int i = 0; i < _characters.Count; i++)
        {
            _characters[i].PrimaryColor = _settings.GetColorForIndex(i);
            _characters[i].Name = _settings.GetNameForIndex(i);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var frameSw = Stopwatch.StartNew();
        if (IsFocusActive && FocusTimeRemaining == TimeSpan.Zero)
        {
            EndFocus();
        }

        TimeSpan nowTimestamp = _clock.Elapsed;
        double rawDt = (nowTimestamp - _lastTickTimestamp).TotalSeconds;
        _lastTickTimestamp = nowTimestamp;

        double dt = Math.Clamp(rawDt, 0.001, 0.05) * _settings.AnimationSpeed;

        // Refresh window/taskbar snapshot (event-driven coalesced).
        var snapshot = _windowTracker.RefreshIfNeeded(_settings.PerformanceMode);
        bool overlayActive = ShouldShowCharacters;
        _overlayManager.SyncMonitors(snapshot, overlayActive);

        bool allSuppressed = _overlayManager.AreAllMonitorsSuppressed(snapshot, overlayActive);

        if (!overlayActive || allSuppressed)
        {
            _aiController.SuspendWorkspaceReactions();
            _overlayManager.UpdateClickThrough(null);
            _currentTargetFps = 0;
            _tickTimer.Interval = TimeSpan.FromMilliseconds(350);
            frameSw.Stop();
            _performanceMonitor.RecordFrame(frameSw.Elapsed.TotalMilliseconds, rendered: false);
            return;
        }

        // 1. Update cursor proximity & selective click-through state.
        _inputManager.Update(_characters, snapshot, _overlayManager, _settings, dt);

        // 2. Update AI, physics, and skeletal poses.
        _aiController.Update(_characters, _particles, snapshot, _inputManager, _settings, dt);
        _physicsEngine.Step(_characters, _particles, snapshot, dt);

        bool hasActiveMotion = _particles.Count > 0 || _characters.Any(c => !c.IsStationaryIdle || c.EmoteTimer > 0);
        bool isHighAction = _inputManager.GrabbedCharacterId.HasValue ||
                            _aiController.CurrentMiniEvent is MiniEventKind.SpectatedDuel or MiniEventKind.TagChase or MiniEventKind.AcrobaticsShow ||
                            _characters.Any(c => c.State is StickmanState.Jump or StickmanState.Fall or StickmanState.Run or StickmanState.Climb or StickmanState.FightAttack or StickmanState.Block or StickmanState.Backflip or StickmanState.HighFive);

        _currentTargetFps = _performanceMonitor.ComputeTargetFps(
            _settings.IsPaused,
            allSuppressed,
            _settings.PerformanceMode,
            hasActiveMotion,
            isHighAction);

        // Smoothly blend skeletal joints toward target procedural pose.
        double baseBlend = Math.Clamp(rawDt * 20.0 * _settings.AnimationSpeed, 0.15, 1.0);
        foreach (var c in _characters)
        {
            var targetPose = ProceduralAnimator.ComputeTargetPose(c);
            double blendAlpha = c.State is StickmanState.Backflip or StickmanState.FightAttack
                ? Math.Max(baseBlend, 0.55)
                : baseBlend;
            c.CurrentPose.BlendFrom(targetPose, blendAlpha);
        }

        // Throttle actual DrawingVisual render cadence when characters are completely idle.
        double minRenderGapMs = _currentTargetFps > 0 ? (1000.0 / _currentTargetFps) * 0.85 : 1000.0;
        bool hoverChanged = _lastHoveredId != _inputManager.HoveredCharacterId;
        bool shouldRender = hoverChanged ||
                            hasActiveMotion ||
                            (DateTime.UtcNow - _lastRenderUtc).TotalMilliseconds >= minRenderGapMs;

        if (shouldRender)
        {
            bool showParticles = !_settings.PerformanceMode || _particles.Any(p => !string.IsNullOrEmpty(p.Text));
            _overlayManager.RenderAll(_characters, _particles, _inputManager.HoveredCharacterId, showParticles);
            _lastRenderUtc = DateTime.UtcNow;
            _lastHoveredId = _inputManager.HoveredCharacterId;
        }

        // Keep tick interval responsive enough for mouse hover while dropping CPU usage during idle/eco mode.
        int desiredTickMs = _currentTargetFps switch
        {
            >= 55 => 16,
            >= 40 => 20,
            >= 24 => 32,
            _ => _settings.EnableMouseInteraction ? 40 : 120
        };
        if ((int)_tickTimer.Interval.TotalMilliseconds != desiredTickMs)
        {
            _tickTimer.Interval = TimeSpan.FromMilliseconds(desiredTickMs);
        }

        frameSw.Stop();
        _performanceMonitor.RecordFrame(frameSw.Elapsed.TotalMilliseconds, shouldRender);
    }

    private StickmanCharacter? OnTryBeginGrab(Point physPt)
    {
        if (!_settings.EnableMouseInteraction || !ShouldShowCharacters)
        {
            return null;
        }

        var hit = InputInteractionManager.HitTestCharacter(_characters, physPt.X, physPt.Y);
        if (hit is not null)
        {
            _inputManager.BeginGrab(hit, physPt);
        }
        return hit;
    }

    private void OnTryOpenContextMenu(OverlayWindow window, Point physPt)
    {
        if (!_settings.EnableMouseInteraction || !ShouldShowCharacters)
        {
            return;
        }

        var hit = InputInteractionManager.HitTestCharacter(_characters, physPt.X, physPt.Y);
        if (hit is not null)
        {
            _inputManager.ShowCharacterContextMenu(
                window,
                hit,
                _windowTracker.CurrentSnapshot.WalkableSurfaces,
                _settings.EnableFighting);
        }
    }

    private void OnChallengeFriendRequested(StickmanCharacter challenger)
    {
        if (!_aiController.ChallengeNearbyFriend(_characters, challenger, _settings))
        {
            challenger.TriggerEmote("?", 0.9);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tickTimer.Stop();
        _windowTracker.Dispose();
        _overlayManager.Dispose();
    }
}
