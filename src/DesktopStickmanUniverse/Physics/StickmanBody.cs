using System.Windows.Media;
using DesktopStickmanUniverse.Animation;
using DesktopStickmanUniverse.Desktop;

namespace DesktopStickmanUniverse.Physics;

public enum StickmanState
{
    Idle,
    LookAround,
    Walk,
    Run,
    Jump,
    Fall,
    Land,
    Sit,
    SitTogether,
    Sleep,
    Climb,
    Grabbed,
    FightAttack,
    FightDodge,
    Block,
    HitReact,
    GetUp,
    Wave,
    Peek,
    HighFive,
    Chat,
    Cheer,
    Stretch,
    Think,
    Dance,
    Backflip,
    Handstand,
    Pushups,
    Brace
}

public enum CombatMoveKind
{
    Punch,
    ComboPunch,
    Uppercut,
    Kick,
    FlyingKick
}

public enum ParticleKind
{
    Dot,
    Ring,
    Star,
    Text
}

public sealed record PersonalityProfile(
    string ArchetypeName,
    double EnergyBias,
    double CuriosityBias,
    double PlayfulnessBias,
    double AggressionBias,
    double LazinessBias,
    double WalkSpeedFactor,
    double ScaleFactor);

public sealed class ParticleEffect
{
    public ParticleKind Kind { get; set; } = ParticleKind.Dot;
    public double X { get; set; }
    public double Y { get; set; }
    public double Vx { get; set; }
    public double Vy { get; set; }
    public double Life { get; set; }
    public double MaxLife { get; set; }
    public double Size { get; set; }
    public double EndSize { get; set; }
    public Color Color { get; set; }
    public string? Text { get; set; }
}

/// <summary>
/// Full runtime state for an autonomous desktop stickman character.
/// World coordinates (X, Y) represent the character's ground/feet anchor in physical screen pixels.
/// </summary>
public sealed class StickmanCharacter
{
    public int Id { get; init; }
    public string Name { get; set; } = "Spark";
    public Color PrimaryColor { get; set; } = Color.FromRgb(244, 92, 92);
    public PersonalityProfile Personality { get; set; } = DefaultPersonalities[0];

    public double X { get; set; }
    public double Y { get; set; }
    public double Vx { get; set; }
    public double Vy { get; set; }
    public int Facing { get; set; } = 1;
    public double DpiScale { get; set; } = 1.0;
    public double VisualScale => Personality.ScaleFactor;

    public bool IsGrounded { get; set; }
    public WalkableSurface? GroundedSurface { get; set; }
    public ClimbableWall? ClimbingWall { get; set; }
    public bool IsGrabbed { get; set; }
    public double FallStartY { get; set; }

    public StickmanState State { get; set; } = StickmanState.Idle;
    public double StateTime { get; set; }
    public double StateDuration { get; set; } = 1.5;

    public double AnimPhase { get; set; }
    public double BlinkTimer { get; set; }
    public double NextBlinkIn { get; set; } = 2.5;

    public Vec2 EyeLookOffset { get; set; }
    public string? EmoteText { get; set; }
    public double EmoteTimer { get; set; }

    // AI Decision Memory & Cooldowns
    public double? MoveTargetX { get; set; }
    public WalkableSurface? TargetSurface { get; set; }
    public ClimbableWall? TargetClimbWall { get; set; }
    public int? PartnerCharacterId { get; set; }
    public int? LookTargetCharacterId { get; set; }
    public CombatMoveKind CombatMove { get; set; } = CombatMoveKind.Punch;
    public bool FollowCursorMode { get; set; }
    public double FightCooldown { get; set; }
    public double ClimbCooldown { get; set; }
    public double JumpCooldown { get; set; }
    public double SleepCooldown { get; set; }
    public double SocialCooldown { get; set; }
    public double MiniEventCooldown { get; set; }
    public double Energy { get; set; } = 0.85;
    public StickmanState PreviousDecisionState { get; set; } = StickmanState.Idle;

    public StickmanPose CurrentPose { get; } = StickmanPose.CreateNeutral();

    public void TriggerEmote(string text, double durationSeconds = 1.1)
    {
        EmoteText = text;
        EmoteTimer = durationSeconds;
    }

    public bool IsStationaryIdle =>
        State is StickmanState.Idle or StickmanState.Sit or StickmanState.SitTogether or StickmanState.Sleep or StickmanState.LookAround
        && Math.Abs(Vx) < 2.0
        && Math.Abs(Vy) < 2.0
        && !IsGrabbed;

    public static IReadOnlyList<PersonalityProfile> DefaultPersonalities { get; } = new[]
    {
        new PersonalityProfile("Spark",  0.90, 0.55, 0.85, 0.85, 0.20, 1.12, 1.00),
        new PersonalityProfile("Scout",  0.75, 0.95, 0.65, 0.35, 0.30, 1.02, 0.96),
        new PersonalityProfile("Mellow", 0.35, 0.45, 0.40, 0.15, 0.90, 0.86, 1.04),
        new PersonalityProfile("Dash",   0.95, 0.70, 0.95, 0.50, 0.15, 1.18, 0.95),
        new PersonalityProfile("Sage",   0.55, 0.85, 0.50, 0.25, 0.55, 0.95, 1.02)
    };

    public static IReadOnlyList<Color> DefaultPalette { get; } = new[]
    {
        Color.FromRgb(244, 92, 92),   // Crimson Coral
        Color.FromRgb(74, 168, 255),  // Sky Cobalt
        Color.FromRgb(64, 212, 146),  // Mint Emerald
        Color.FromRgb(255, 184, 64),  // Amber Gold
        Color.FromRgb(186, 128, 255), // Violet Lavender
        Color.FromRgb(255, 118, 188), // Rose Pink
        Color.FromRgb(64, 218, 232),  // Cyan Aqua
        Color.FromRgb(245, 245, 250)  // Crisp Chalk White
    };
}
