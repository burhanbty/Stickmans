using DesktopStickmanUniverse.Desktop;
using DesktopStickmanUniverse.Physics;

namespace DesktopStickmanUniverse.AI;

/// <summary>Turns settled changes to the active window into brief character reactions.</summary>
internal sealed class WorkspaceReactions
{
    private const double SettleSeconds = 0.36;
    private const double CaptionSettleSeconds = 0.68;
    private const double CooldownSeconds = 2.5;

    private long? _sequenceId;
    private TrackedWindow? _observed;
    private TrackedWindow? _settled;
    private MonitorInfoData? _activeMonitor;
    private double _settleTime;
    private double _cooldown;

    public void Reset()
    {
        _sequenceId = null;
        _observed = null;
        _settled = null;
        _activeMonitor = null;
        _settleTime = 0;
        _cooldown = 0;
    }

    public void Update(IReadOnlyList<StickmanCharacter> characters, DesktopEnvironmentSnapshot snapshot, bool enabled, double elapsed)
    {
        if (!enabled)
        {
            Reset();
            return;
        }

        _cooldown = Math.Max(0, _cooldown - elapsed);
        if (_sequenceId != snapshot.SequenceId)
        {
            bool firstSnapshot = !_sequenceId.HasValue;
            _sequenceId = snapshot.SequenceId;
            Observe(snapshot, firstSnapshot);
        }

        if (_settleTime <= 0 || _observed is not { } window || _activeMonitor is not { } monitor)
        {
            return;
        }

        _settleTime -= elapsed;
        if (_settleTime > 0)
        {
            return;
        }

        var previous = _settled;
        _settled = window;
        if (_cooldown > 0 || Classify(previous, window, monitor.DpiScale) is not { } reaction)
        {
            return;
        }

        var character = FindObserver(characters, window, monitor, reaction);
        if (character is not null)
        {
            React(character, window, reaction);
            _cooldown = CooldownSeconds;
        }
    }

    private void Observe(DesktopEnvironmentSnapshot snapshot, bool firstSnapshot)
    {
        var window = snapshot.Windows.FirstOrDefault(w => w.IsForeground);
        _activeMonitor = snapshot.Monitors.FirstOrDefault(m => m.DeviceName == window?.MonitorDeviceName && !m.HasFullscreenWindow);
        if (window is null || _activeMonitor is null || snapshot.IsAnyExclusiveFullscreen)
        {
            _observed = null;
            _settleTime = 0;
            return;
        }

        if (firstSnapshot || (_observed?.Hwnd != window.Hwnd && _settled?.Hwnd == window.Hwnd))
        {
            _settled = window;
            _settleTime = 0;
        }
        else if (_observed?.Hwnd != window.Hwnd || _observed.Bounds != window.Bounds)
        {
            _settleTime = SettleSeconds;
        }
        else if (CaptionChanged(_observed, window))
        {
            _settleTime = CaptionSettleSeconds;
        }

        _observed = window;
    }

    private static Reaction? Classify(TrackedWindow? previous, TrackedWindow current, double dpi)
    {
        if (previous?.Hwnd != current.Hwnd)
        {
            return Reaction.Activated;
        }

        var before = previous.Bounds;
        var after = current.Bounds;
        double threshold = 24 * dpi;
        if (Math.Max(Math.Abs(after.Width - before.Width), Math.Abs(after.Height - before.Height)) >= threshold)
        {
            return Reaction.Resized;
        }
        if (Math.Max(Math.Abs(after.Left - before.Left), Math.Abs(after.Top - before.Top)) >= threshold)
        {
            return Reaction.Moved;
        }
        return CaptionChanged(previous, current) ? Reaction.CaptionChanged : null;
    }

    private static bool CaptionChanged(TrackedWindow previous, TrackedWindow current) =>
        !string.IsNullOrWhiteSpace(previous.Title) && !string.IsNullOrWhiteSpace(current.Title) &&
        !string.Equals(previous.Title.Trim(), current.Title.Trim(), StringComparison.Ordinal);

    private static StickmanCharacter? FindObserver(
        IReadOnlyList<StickmanCharacter> characters, TrackedWindow window, MonitorInfoData monitor, Reaction reaction)
    {
        double range = 420 * monitor.DpiScale;
        return characters.Where(c => c.IsGrounded && !c.IsGrabbed && !c.FollowCursorMode &&
                c.PartnerCharacterId is null && c.EmoteTimer <= 0 && c.TargetSurface is null &&
                c.State is StickmanState.Idle or StickmanState.LookAround or StickmanState.Walk or StickmanState.Sit &&
                monitor.Bounds.Contains(c.X, c.Y - 1))
            .Select(c => new
            {
                Character = c,
                Riding = c.GroundedSurface is { Kind: SurfaceKind.WindowTop } surface && surface.SourceHwnd == window.Hwnd,
                Distance = DistanceFromWindow(c, window.Bounds)
            })
            .Where(c => c.Riding || c.Distance <= range || reaction == Reaction.Activated)
            .OrderByDescending(c => c.Riding)
            .ThenBy(c => c.Distance - 32 * monitor.DpiScale * c.Character.Personality.CuriosityBias)
            .Select(c => c.Character)
            .FirstOrDefault();
    }

    private static double DistanceFromWindow(StickmanCharacter character, PixelRect window)
    {
        double dx = character.X - Math.Clamp(character.X, window.Left, window.Right);
        double dy = character.Y - Math.Clamp(character.Y, window.Top, window.Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static void React(StickmanCharacter character, TrackedWindow window, Reaction reaction)
    {
        bool riding = character.GroundedSurface is { Kind: SurfaceKind.WindowTop } surface && surface.SourceHwnd == window.Hwnd;
        (StickmanState state, string emote) = reaction switch
        {
            Reaction.Moved when riding => (StickmanState.Brace, "Whoa!"),
            Reaction.Moved => (StickmanState.Peek, "!"),
            Reaction.Resized => (StickmanState.Think, "Hmm?"),
            Reaction.CaptionChanged => (StickmanState.Peek, "?"),
            _ => (StickmanState.Wave, "Hi!")
        };

        character.Facing = window.Bounds.Left + window.Bounds.Width * 0.5 >= character.X ? 1 : -1;
        character.MoveTargetX = null;
        character.TargetClimbWall = null;
        character.Vx = 0;
        character.State = state;
        character.StateTime = 0;
        character.StateDuration = 1.35;
        character.TriggerEmote(emote, 1.2);
    }

    private enum Reaction { Activated, Moved, Resized, CaptionChanged }
}
