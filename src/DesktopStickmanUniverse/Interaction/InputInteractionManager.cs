using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DesktopStickmanUniverse.AI;
using DesktopStickmanUniverse.Desktop;
using DesktopStickmanUniverse.Interop;
using DesktopStickmanUniverse.Overlay;
using DesktopStickmanUniverse.Physics;
using DesktopStickmanUniverse.Settings;
using static DesktopStickmanUniverse.Interop.Win32Native;

namespace DesktopStickmanUniverse.Interaction;

/// <summary>
/// Tracks cursor position/velocity, enforces safe selective click-through,
/// and handles character grab/drag/throw, clicks, and context menu actions.
/// </summary>
public sealed class InputInteractionManager
{
    public double CursorX { get; private set; }
    public double CursorY { get; private set; }
    public double CursorVx { get; private set; }
    public double CursorVy { get; private set; }
    public bool HasCursor { get; private set; }
    public int? HoveredCharacterId { get; private set; }
    public int? GrabbedCharacterId => _grabbedCharacter?.Id;

    private StickmanCharacter? _grabbedCharacter;
    private OverlayWindow? _activeGrabWindow;
    private Point _grabStartPt;
    private DateTime _grabStartUtc;
    private double _dragVx;
    private double _dragVy;
    private double _lastCursorSampleX;
    private double _lastCursorSampleY;
    private DateTime _lastCursorSampleUtc = DateTime.UtcNow;
    private bool _contextMenuOpen;
    private ContextMenu? _activeContextMenu;
    private DateTime _contextMenuOpenedUtc = DateTime.MinValue;

    public event Action? OpenSettingsRequested;
    public event Action<StickmanCharacter>? CharacterInteracted;
    public event Action<StickmanCharacter>? ChallengeFriendRequested;

    public void AttachOverlay(OverlayManager overlayManager)
    {
        overlayManager.MouseLeftDown += OnMouseLeftDown;
        overlayManager.MouseMove += OnMouseMove;
        overlayManager.MouseLeftUp += OnMouseLeftUp;
        overlayManager.MouseRightUp += OnMouseRightUp;
    }

    public void Update(
        IReadOnlyList<StickmanCharacter> characters,
        DesktopEnvironmentSnapshot snapshot,
        OverlayManager overlayManager,
        AppSettings settings,
        double dt)
    {
        if (!GetCursorPos(out var pt))
        {
            overlayManager.UpdateClickThrough(null);
            return;
        }

        var now = DateTime.UtcNow;
        double elapsed = Math.Max(0.001, (now - _lastCursorSampleUtc).TotalSeconds);
        if (HasCursor)
        {
            double instVx = (pt.X - _lastCursorSampleX) / elapsed;
            double instVy = (pt.Y - _lastCursorSampleY) / elapsed;
            CursorVx = CursorVx * 0.65 + instVx * 0.35;
            CursorVy = CursorVy * 0.65 + instVy * 0.35;
        }

        CursorX = pt.X;
        CursorY = pt.Y;
        _lastCursorSampleX = pt.X;
        _lastCursorSampleY = pt.Y;
        _lastCursorSampleUtc = now;
        HasCursor = true;

        bool leftDown = IsKeyDown(VK_LBUTTON);

        // Safety check: if grabbed character lost mouse button without receiving MouseLeftUp, release cleanly.
        if (_grabbedCharacter is not null)
        {
            if (!leftDown || !settings.EnableMouseInteraction || settings.IsPaused || !settings.CharactersEnabled)
            {
                ReleaseGrab(new Point(CursorX, CursorY));
            }
            else
            {
                UpdateGrabbedPosition(new Point(CursorX, CursorY), dt);
                var mon = snapshot.FindMonitorAt(CursorX, CursorY);
                overlayManager.UpdateClickThrough(mon?.DeviceName);
                return;
            }
        }

        if (!settings.EnableMouseInteraction || settings.IsPaused || !settings.CharactersEnabled || _contextMenuOpen)
        {
            if (_contextMenuOpen && _activeContextMenu is not null &&
                (DateTime.UtcNow - _contextMenuOpenedUtc).TotalMilliseconds > 180 &&
                leftDown && !_activeContextMenu.IsMouseOver)
            {
                _activeContextMenu.IsOpen = false;
            }

            HoveredCharacterId = null;
            overlayManager.UpdateClickThrough(null);
            return;
        }

        // Never steal input if the user is already holding left-click on another window (e.g. selecting text/files).
        if (leftDown)
        {
            HoveredCharacterId = null;
            overlayManager.UpdateClickThrough(null);
            return;
        }

        var hit = HitTestCharacter(characters, CursorX, CursorY);
        HoveredCharacterId = hit?.Id;

        if (hit is not null)
        {
            var mon = snapshot.FindMonitorAt(CursorX, CursorY);
            overlayManager.UpdateClickThrough(mon?.DeviceName);
        }
        else
        {
            overlayManager.UpdateClickThrough(null);
        }
    }

    private void OnMouseLeftDown(OverlayWindow window, Point physPt)
    {
        if (_grabbedCharacter is not null)
        {
            return;
        }

        // Re-verify using current hovered state; engine calls HitTest when clicking.
        _activeGrabWindow = window;
        _grabStartPt = physPt;
        _grabStartUtc = DateTime.UtcNow;
        _dragVx = 0;
        _dragVy = 0;
        window.CaptureMouse();
        TryBeginGrabRequested?.Invoke(physPt);
    }

    internal event Func<Point, StickmanCharacter?>? TryBeginGrabRequested;

    internal void BeginGrab(StickmanCharacter character, Point physPt)
    {
        _grabbedCharacter = character;
        _grabStartPt = physPt;
        _grabStartUtc = DateTime.UtcNow;
        character.IsGrabbed = true;
        character.IsGrounded = false;
        character.GroundedSurface = null;
        character.ClimbingWall = null;
        character.State = StickmanState.Grabbed;
        character.StateTime = 0;
        character.Vx = 0;
        character.Vy = 0;
        character.FollowCursorMode = false;
        UpdateGrabbedPosition(physPt, 0.016);
    }

    private void OnMouseMove(OverlayWindow window, Point physPt)
    {
        if (_grabbedCharacter is null)
        {
            return;
        }

        UpdateGrabbedPosition(physPt, 0.016);
    }

    private void OnMouseLeftUp(OverlayWindow window, Point physPt)
    {
        if (_grabbedCharacter is null)
        {
            if (window.IsMouseCaptured)
            {
                window.ReleaseMouseCapture();
            }
            return;
        }

        ReleaseGrab(physPt);
    }

    private void UpdateGrabbedPosition(Point physPt, double dt)
    {
        if (_grabbedCharacter is null)
        {
            return;
        }

        double dpi = _grabbedCharacter.DpiScale;
        double s = _grabbedCharacter.VisualScale;
        double targetX = physPt.X;
        double targetY = physPt.Y + 42.0 * s * dpi;

        double safeDt = Math.Clamp(dt, 0.004, 0.05);
        double vx = (targetX - _grabbedCharacter.X) / safeDt;
        double vy = (targetY - _grabbedCharacter.Y) / safeDt;

        _dragVx = _dragVx * 0.55 + vx * 0.45;
        _dragVy = _dragVy * 0.55 + vy * 0.45;

        _grabbedCharacter.X = targetX;
        _grabbedCharacter.Y = targetY;
        _grabbedCharacter.Vx = _dragVx;
        _grabbedCharacter.Vy = _dragVy;

        if (Math.Abs(_dragVx) > 15)
        {
            _grabbedCharacter.Facing = _dragVx >= 0 ? 1 : -1;
        }
    }

    private void ReleaseGrab(Point physPt)
    {
        var c = _grabbedCharacter;
        _grabbedCharacter = null;

        if (_activeGrabWindow is not null && _activeGrabWindow.IsMouseCaptured)
        {
            _activeGrabWindow.ReleaseMouseCapture();
        }
        _activeGrabWindow = null;

        if (c is null)
        {
            return;
        }

        double dpi = c.DpiScale;
        c.IsGrabbed = false;

        double dx = physPt.X - _grabStartPt.X;
        double dy = physPt.Y - _grabStartPt.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double heldSec = (DateTime.UtcNow - _grabStartUtc).TotalSeconds;

        if (dist < 7.0 * dpi && heldSec < 0.28)
        {
            // Short click reaction.
            c.Vx = 0;
            c.Vy = -320.0 * dpi;
            c.FallStartY = c.Y;
            c.State = StickmanState.Jump;
            c.StateTime = 0;
            c.StateDuration = 0.6;
            c.TriggerEmote("Hi!", 0.9);
            CharacterInteracted?.Invoke(c);
            return;
        }

        c.Vx = Math.Clamp(_dragVx, -1100.0 * dpi, 1100.0 * dpi);
        c.Vy = Math.Clamp(_dragVy, -1100.0 * dpi, 900.0 * dpi);
        c.FallStartY = c.Y;
        c.State = StickmanState.Fall;
        c.StateTime = 0;
        c.StateDuration = 1.5;
        CharacterInteracted?.Invoke(c);
    }

    private void OnMouseRightUp(OverlayWindow window, Point physPt)
    {
        TryOpenContextMenuRequested?.Invoke(window, physPt);
    }

    internal event Action<OverlayWindow, Point>? TryOpenContextMenuRequested;

    public void ShowCharacterContextMenu(
        OverlayWindow ownerWindow,
        StickmanCharacter character,
        IReadOnlyList<WalkableSurface> surfaces,
        bool enableFighting = true)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = ownerWindow,
            Placement = PlacementMode.MousePoint,
            HasDropShadow = true
        };

        var header = new MenuItem
        {
            Header = $"{character.Name} ({character.Personality.ArchetypeName})",
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold
        };
        menu.Items.Add(header);
        menu.Items.Add(new Separator());

        var followItem = new MenuItem
        {
            Header = character.FollowCursorMode ? "Stop Following Cursor" : "Follow Cursor",
            IsChecked = character.FollowCursorMode
        };
        followItem.Click += (_, _) =>
        {
            character.FollowCursorMode = !character.FollowCursorMode;
            character.TriggerEmote(character.FollowCursorMode ? "?" : "!", 0.9);
        };
        menu.Items.Add(followItem);

        var waveItem = new MenuItem { Header = "Wave & Say Hi" };
        waveItem.Click += (_, _) =>
        {
            character.State = StickmanState.Wave;
            character.StateTime = 0;
            character.StateDuration = 1.6;
            character.Vx = 0;
            character.TriggerEmote("Hi!", 1.2);
        };
        menu.Items.Add(waveItem);

        var sitItem = new MenuItem { Header = "Sit & Rest" };
        sitItem.Click += (_, _) =>
        {
            if (character.IsGrounded)
            {
                character.State = StickmanState.Sit;
                character.StateTime = 0;
                character.StateDuration = 6.0;
                character.Vx = 0;
            }
        };
        menu.Items.Add(sitItem);

        var sleepItem = new MenuItem { Header = "Take a Nap" };
        sleepItem.Click += (_, _) =>
        {
            if (character.IsGrounded)
            {
                character.PartnerCharacterId = null;
                character.LookTargetCharacterId = null;
                character.State = StickmanState.Sleep;
                character.StateTime = 0;
                character.StateDuration = 9.0;
                character.Vx = 0;
            }
        };
        menu.Items.Add(sleepItem);

        var backflipItem = new MenuItem { Header = "Do Backflip" };
        backflipItem.Click += (_, _) =>
        {
            if (!character.IsGrabbed && character.IsGrounded)
            {
                AiBehaviorController.LaunchBackflip(character);
                character.TriggerEmote("!", 0.7);
            }
        };
        menu.Items.Add(backflipItem);

        var danceItem = new MenuItem { Header = "Dance" };
        danceItem.Click += (_, _) =>
        {
            if (character.IsGrounded)
            {
                character.PartnerCharacterId = null;
                character.LookTargetCharacterId = null;
                character.Vx = 0;
                character.State = StickmanState.Dance;
                character.StateTime = 0;
                character.StateDuration = 3.6;
                character.TriggerEmote("♪", 1.2);
            }
        };
        menu.Items.Add(danceItem);

        var pushupsItem = new MenuItem { Header = "Workout (Pushups)" };
        pushupsItem.Click += (_, _) =>
        {
            if (character.IsGrounded)
            {
                character.PartnerCharacterId = null;
                character.LookTargetCharacterId = null;
                character.Vx = 0;
                character.State = StickmanState.Pushups;
                character.StateTime = 0;
                character.StateDuration = 3.6;
            }
        };
        menu.Items.Add(pushupsItem);

        if (enableFighting)
        {
            var challengeItem = new MenuItem { Header = "Challenge Nearby Friend" };
            challengeItem.Click += (_, _) => ChallengeFriendRequested?.Invoke(character);
            menu.Items.Add(challengeItem);
        }

        var dropItem = new MenuItem { Header = "Jump to Taskbar" };
        dropItem.Click += (_, _) =>
        {
            var taskbar = surfaces.FirstOrDefault(s => s.Kind is SurfaceKind.Taskbar or SurfaceKind.MonitorFloor);
            if (taskbar is not null)
            {
                character.IsGrounded = false;
                character.GroundedSurface = null;
                character.ClimbingWall = null;
                character.FallStartY = character.Y;
                character.Vy = -260.0 * character.DpiScale;
                character.Vx = Math.Clamp((taskbar.Left + taskbar.Right) * 0.5 - character.X, -220, 220);
                character.State = StickmanState.Jump;
                character.StateTime = 0;
                character.StateDuration = 1.0;
            }
        };
        menu.Items.Add(dropItem);

        menu.Items.Add(new Separator());

        var settingsItem = new MenuItem { Header = "Settings..." };
        settingsItem.Click += (_, _) => OpenSettingsRequested?.Invoke();
        menu.Items.Add(settingsItem);

        _contextMenuOpen = true;
        _activeContextMenu = menu;
        _contextMenuOpenedUtc = DateTime.UtcNow;
        menu.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            if (ReferenceEquals(_activeContextMenu, menu))
            {
                _activeContextMenu = null;
            }
        };
        menu.IsOpen = true;
    }

    public static StickmanCharacter? HitTestCharacter(
        IReadOnlyList<StickmanCharacter> characters,
        double physX,
        double physY)
    {
        for (int i = characters.Count - 1; i >= 0; i--)
        {
            var c = characters[i];
            double dpi = c.DpiScale;
            double s = c.VisualScale;
            double halfW = 20.0 * s * dpi;
            double topOffset = 58.0 * s * dpi;
            double bottomOffset = 10.0 * s * dpi;

            if (physX >= c.X - halfW &&
                physX <= c.X + halfW &&
                physY >= c.Y - topOffset &&
                physY <= c.Y + bottomOffset)
            {
                return c;
            }
        }

        return null;
    }
}
