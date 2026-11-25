using System.Windows.Media;
using DesktopStickmanUniverse.Animation;
using DesktopStickmanUniverse.Desktop;
using DesktopStickmanUniverse.Interaction;
using DesktopStickmanUniverse.Physics;
using DesktopStickmanUniverse.Settings;

namespace DesktopStickmanUniverse.AI;

/// <summary>
/// Per-character autonomous AI controller combining personality traits,
/// desktop window awareness, cursor reactions, jumping/climbing navigation,
/// and anti-repetition decision logic.
/// </summary>
public sealed class AiBehaviorController
{
    private readonly Random _rng;
    private readonly MiniEventDirector _eventDirector;
    private readonly HashSet<IntPtr> _knownWindowHwnds = new();
    private long _lastSnapshotSeq = -1;
    private double _zzzSpawnAccumulator;

    public AiBehaviorController(Random? rng = null)
    {
        _rng = rng ?? new Random();
        _eventDirector = new MiniEventDirector(_rng);
    }

    public MiniEventKind CurrentMiniEvent => _eventDirector.ActiveEvent;

    public MiniEventStartResult TryStartMiniEvent(
        MiniEventKind kind,
        IReadOnlyList<StickmanCharacter> characters,
        DesktopEnvironmentSnapshot snapshot,
        AppSettings settings)
        => _eventDirector.TryStartEvent(kind, characters, snapshot, settings);

    public bool ChallengeNearbyFriend(
        IReadOnlyList<StickmanCharacter> characters,
        StickmanCharacter challenger,
        AppSettings settings)
        => _eventDirector.TryStartDuel(characters, challenger, settings);

    public void Reset()
    {
        _eventDirector.Reset();
        _knownWindowHwnds.Clear();
        _lastSnapshotSeq = -1;
    }

    public void Update(
        IReadOnlyList<StickmanCharacter> characters,
        List<ParticleEffect> particles,
        DesktopEnvironmentSnapshot snapshot,
        InputInteractionManager input,
        AppSettings settings,
        double dt)
    {
        if (snapshot.Monitors.Count == 0)
        {
            return;
        }

        // 1. React to newly opened windows on desktop.
        if (snapshot.SequenceId != _lastSnapshotSeq)
        {
            _lastSnapshotSeq = snapshot.SequenceId;
            HandleWindowLifecycleChanges(characters, snapshot, settings);
        }

        // 2. Update emergent multi-character mini-events.
        _eventDirector.Update(characters, particles, snapshot, settings, dt);

        // 3. Step each character's autonomous behavior, timers, cursor awareness, and navigation.
        _zzzSpawnAccumulator += dt;
        bool spawnZzzTick = _zzzSpawnAccumulator >= 1.1;
        if (spawnZzzTick)
        {
            _zzzSpawnAccumulator = 0;
        }

        foreach (var c in characters)
        {
            UpdateTimers(c, dt);

            if (c.IsGrabbed)
            {
                c.AnimPhase += dt;
                continue;
            }

            if (c.State == StickmanState.Sleep && spawnZzzTick)
            {
                SpawnSleepZzz(particles, c);
            }

            if (settings.EnableMouseInteraction && input.HasCursor)
            {
                UpdateCursorAwareness(c, input, dt);
            }
            else
            {
                c.EyeLookOffset = Vec2.Lerp(c.EyeLookOffset, Vec2.Zero, Math.Clamp(dt * 8.0, 0, 1));
            }

            UpdateSocialAndSpectatorAwareness(c, characters, input, settings, dt);

            // Characters participating in an active coordinated mini-event delegate high-level steering.
            if (_eventDirector.IsParticipating(c.Id))
            {
                c.AnimPhase += dt;
                if (c.StateTime >= c.StateDuration &&
                    c.State is StickmanState.FightAttack or StickmanState.FightDodge or StickmanState.Block or StickmanState.HitReact or StickmanState.HighFive or StickmanState.Cheer)
                {
                    c.State = StickmanState.Idle;
                    c.StateTime = 0;
                    c.StateDuration = 0.25;
                }
                continue;
            }

            StepCharacterBehavior(c, characters, particles, snapshot, input, settings, dt);
            c.AnimPhase += dt;
        }
    }

    private void HandleWindowLifecycleChanges(
        IReadOnlyList<StickmanCharacter> characters,
        DesktopEnvironmentSnapshot snapshot,
        AppSettings settings)
    {
        var currentHwnds = snapshot.Windows.Select(w => w.Hwnd).ToHashSet();

        if (_knownWindowHwnds.Count > 0)
        {
            foreach (var win in snapshot.Windows)
            {
                if (_knownWindowHwnds.Contains(win.Hwnd))
                {
                    continue;
                }

                // A brand-new window opened! Curious idle/walking characters look at or explore it.
                var topSurf = snapshot.WalkableSurfaces.FirstOrDefault(s => s.SourceHwnd == win.Hwnd);
                foreach (var c in characters)
                {
                    if (c.IsGrabbed || c.State is StickmanState.Sleep or StickmanState.Climb or StickmanState.FightAttack)
                    {
                        continue;
                    }

                    if (_rng.NextDouble() < c.Personality.CuriosityBias * 0.75)
                    {
                        c.Facing = ((win.Bounds.Left + win.Bounds.Right) * 0.5 >= c.X) ? 1 : -1;
                        c.TriggerEmote("!", 0.85);
                        if (topSurf is not null && c.IsGrounded && _rng.NextDouble() < 0.6)
                        {
                            c.TargetSurface = topSurf;
                            c.MoveTargetX = Math.Clamp(c.X, topSurf.Left + 24 * c.DpiScale, topSurf.Right - 24 * c.DpiScale);
                            c.State = StickmanState.Run;
                            c.StateTime = 0;
                            c.StateDuration = 4.0;
                        }
                    }
                }
            }
        }

        _knownWindowHwnds.Clear();
        foreach (var h in currentHwnds)
        {
            _knownWindowHwnds.Add(h);
        }
    }

    private void UpdateTimers(StickmanCharacter c, double dt)
    {
        c.StateTime += dt;
        c.FightCooldown = Math.Max(0, c.FightCooldown - dt);
        c.SocialCooldown = Math.Max(0, c.SocialCooldown - dt);
        c.ClimbCooldown = Math.Max(0, c.ClimbCooldown - dt);
        c.JumpCooldown = Math.Max(0, c.JumpCooldown - dt);
        c.SleepCooldown = Math.Max(0, c.SleepCooldown - dt);
        c.MiniEventCooldown = Math.Max(0, c.MiniEventCooldown - dt);

        if (c.EmoteTimer > 0)
        {
            c.EmoteTimer = Math.Max(0, c.EmoteTimer - dt);
            if (c.EmoteTimer <= 0)
            {
                c.EmoteText = null;
            }
        }

        // Natural blinking.
        if (c.BlinkTimer > 0)
        {
            c.BlinkTimer -= dt;
        }
        else
        {
            c.NextBlinkIn -= dt;
            if (c.NextBlinkIn <= 0)
            {
                c.BlinkTimer = 0.13;
                c.NextBlinkIn = 2.0 + _rng.NextDouble() * 3.5;
            }
        }

        // Energy recovers when resting/sleeping/stretching and drains gently when active.
        if (c.State is StickmanState.Sleep or StickmanState.Sit or StickmanState.SitTogether or StickmanState.Idle or StickmanState.Think or StickmanState.Stretch)
        {
            c.Energy = Math.Min(1.0, c.Energy + dt * 0.045);
        }
        else if (c.State is StickmanState.Run or StickmanState.Climb or StickmanState.FightAttack or StickmanState.Backflip or StickmanState.Handstand or StickmanState.Pushups or StickmanState.Dance)
        {
            c.Energy = Math.Max(0.1, c.Energy - dt * 0.035);
        }
    }

    private void UpdateCursorAwareness(StickmanCharacter c, InputInteractionManager input, double dt)
    {
        double dpi = c.DpiScale;
        double dx = input.CursorX - c.X;
        double dy = input.CursorY - (c.Y - 40.0 * c.VisualScale * dpi);
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < 260.0 * dpi)
        {
            Vec2 desired = new Vec2(dx, dy).Normalized() * 1.4;
            c.EyeLookOffset = Vec2.Lerp(c.EyeLookOffset, desired, Math.Clamp(dt * 12.0, 0, 1));
        }
        else
        {
            c.EyeLookOffset = Vec2.Lerp(c.EyeLookOffset, Vec2.Zero, Math.Clamp(dt * 8.0, 0, 1));
        }

        // Dodge/react playfully if user sweeps mouse rapidly right through the character.
        double cursorSpeed = Math.Sqrt(input.CursorVx * input.CursorVx + input.CursorVy * input.CursorVy);
        if (dist < 65.0 * dpi && cursorSpeed > 950.0 * dpi && c.IsGrounded && c.JumpCooldown <= 0 && c.State != StickmanState.Sleep)
        {
            if (c.Personality.EnergyBias > 0.6 && _rng.NextDouble() < 0.35)
            {
                c.Facing = dx >= 0 ? -1 : 1;
                c.Vx = c.Facing * 170.0 * dpi;
                c.Vy = -260.0 * dpi;
                c.IsGrounded = false;
                c.GroundedSurface = null;
                c.FallStartY = c.Y;
                c.State = StickmanState.Jump;
                c.StateTime = 0;
                c.StateDuration = 0.6;
                c.JumpCooldown = 2.0;
                c.TriggerEmote("!", 0.6);
            }
        }
    }

    private void StepCharacterBehavior(
        StickmanCharacter c,
        IReadOnlyList<StickmanCharacter> characters,
        List<ParticleEffect> particles,
        DesktopEnvironmentSnapshot snapshot,
        InputInteractionManager input,
        AppSettings settings,
        double dt)
    {
        double dpi = c.DpiScale;

        // Follow cursor mode (from context menu).
        if (c.FollowCursorMode && settings.EnableMouseInteraction && input.HasCursor && c.IsGrounded)
        {
            c.PartnerCharacterId = null;
            c.LookTargetCharacterId = null;
            double dx = input.CursorX - c.X;
            if (Math.Abs(dx) > 36.0 * dpi)
            {
                c.Facing = dx >= 0 ? 1 : -1;
                double speed = (Math.Abs(dx) > 160 * dpi ? 145.0 : 88.0) * dpi * c.Personality.WalkSpeedFactor;
                c.State = Math.Abs(dx) > 160 * dpi ? StickmanState.Run : StickmanState.Walk;
                c.Vx = c.Facing * speed;
                TryTraverseOrJumpToward(c, input.CursorX, input.CursorY, snapshot, dpi);
            }
            else
            {
                c.Vx = 0;
                c.Facing = dx >= 0 ? 1 : -1;
                c.State = StickmanState.LookAround;
            }
            return;
        }

        if (!c.IsGrounded)
        {
            return;
        }

        // Wait for recovery, combat, and quick emote states to finish naturally.
        if (c.State is StickmanState.Land or StickmanState.GetUp or StickmanState.HitReact or
            StickmanState.FightAttack or StickmanState.FightDodge or StickmanState.Block or
            StickmanState.HighFive or StickmanState.Cheer)
        {
            c.Vx = 0;
            if (c.StateTime >= c.StateDuration)
            {
                c.PartnerCharacterId = null;
                c.LookTargetCharacterId = null;
                PickNextGroundedDecision(c, characters, snapshot, input, settings);
            }
            return;
        }

        // If chatting or sitting together with a friend, abort gracefully if friend leaves or is grabbed.
        if (c.State is StickmanState.Chat or StickmanState.SitTogether && c.PartnerCharacterId.HasValue)
        {
            var partner = characters.FirstOrDefault(p => p.Id == c.PartnerCharacterId.Value);
            if (partner is null || partner.IsGrabbed || !partner.IsGrounded || Math.Abs(partner.X - c.X) > 125.0 * dpi)
            {
                c.PartnerCharacterId = null;
                c.LookTargetCharacterId = null;
                PickNextGroundedDecision(c, characters, snapshot, input, settings);
                return;
            }
        }

        // Spontaneous proximity social greetings or playful scuffles when no central mini-event owns the stage.
        if (_eventDirector.ActiveEvent == MiniEventKind.None &&
            c.SocialCooldown <= 0 &&
            !c.PartnerCharacterId.HasValue &&
            c.State is StickmanState.Idle or StickmanState.LookAround or StickmanState.Walk or StickmanState.Run or StickmanState.Sit)
        {
            if (TryTriggerProximitySocialInteraction(c, characters, particles, settings, dt))
            {
                return;
            }
        }

        // Active surface traversal / targeted movement.
        if (c.State is StickmanState.Walk or StickmanState.Run)
        {
            double baseSpeed = (c.State == StickmanState.Run ? 142.0 : 78.0) * dpi * c.Personality.WalkSpeedFactor;
            c.Vx = c.Facing * baseSpeed;

            if (c.GroundedSurface is not null)
            {
                // Avoid casually walking off narrow edges unless aiming for a jump/climb.
                double margin = 14.0 * dpi;
                if (c.TargetSurface is null)
                {
                    if (c.Facing < 0 && c.X <= c.GroundedSurface.Left + margin)
                    {
                        if (TryJumpToAdjacentSurface(c, snapshot, dpi) || TryStartClimbingNearbyWall(c, snapshot, dpi))
                        {
                            return;
                        }
                        c.Facing = 1;
                    }
                    else if (c.Facing > 0 && c.X >= c.GroundedSurface.Right - margin)
                    {
                        if (TryJumpToAdjacentSurface(c, snapshot, dpi) || TryStartClimbingNearbyWall(c, snapshot, dpi))
                        {
                            return;
                        }
                        c.Facing = -1;
                    }
                }
                else
                {
                    TryNavigateToTargetSurface(c, snapshot, dpi);
                }
            }

            if (c.MoveTargetX.HasValue && Math.Abs(c.X - c.MoveTargetX.Value) <= 14.0 * dpi)
            {
                c.MoveTargetX = null;
                c.TargetSurface = null;
                c.Vx = 0;
                PickNextGroundedDecision(c, characters, snapshot, input, settings);
                return;
            }

            // Opportunistically climb an inviting window edge when walking past one on taskbar.
            if (c.ClimbCooldown <= 0 && c.GroundedSurface?.Kind != SurfaceKind.WindowTop && _rng.NextDouble() < 0.35 * c.Personality.CuriosityBias * dt)
            {
                if (TryStartClimbingNearbyWall(c, snapshot, dpi))
                {
                    return;
                }
            }
        }
        else
        {
            c.Vx = 0;
        }

        if (c.StateTime >= c.StateDuration)
        {
            c.PartnerCharacterId = null;
            c.LookTargetCharacterId = null;
            PickNextGroundedDecision(c, characters, snapshot, input, settings);
        }
    }

    private void PickNextGroundedDecision(
        StickmanCharacter c,
        IReadOnlyList<StickmanCharacter> characters,
        DesktopEnvironmentSnapshot snapshot,
        InputInteractionManager input,
        AppSettings settings)
    {
        double dpi = c.DpiScale;
        double activityBoost = settings.ActivityLevel switch
        {
            ActivityLevel.Calm => 0.65,
            ActivityLevel.Lively => 1.45,
            _ => 1.0
        };

        // Weighted random utility selection avoiding immediate repeats of stationary states.
        var weights = new List<(Func<bool> TryExecute, double Weight)>
        {
            (() => StartWalk(c, snapshot, false), 2.8 * activityBoost),
            (() => StartWalk(c, snapshot, true), 1.6 * c.Personality.EnergyBias * activityBoost),
            (() => StartIdleOrLook(c), c.PreviousDecisionState == StickmanState.Idle ? 0.45 : 1.4),
            (() => StartSit(c), c.PreviousDecisionState == StickmanState.Sit ? 0.35 : 1.5 * (0.4 + c.Personality.LazinessBias)),
            (() => StartSleep(c), c.SleepCooldown <= 0 && c.Energy < 0.6 ? 1.4 * c.Personality.LazinessBias : 0.12),
            (() => StartWave(c), c.PreviousDecisionState == StickmanState.Wave ? 0.2 : 0.65 * c.Personality.PlayfulnessBias),
            (() => StartStretch(c), c.PreviousDecisionState == StickmanState.Stretch ? 0.25 : 1.3 * (0.6 + c.Personality.LazinessBias)),
            (() => StartThink(c), c.PreviousDecisionState == StickmanState.Think ? 0.25 : 1.2 * (0.6 + c.Personality.CuriosityBias)),
            (() => StartDance(c), c.PreviousDecisionState == StickmanState.Dance ? 0.2 : 1.25 * c.Personality.PlayfulnessBias * activityBoost),
            (() => TryBackflip(c), c.JumpCooldown <= 0 && c.Energy > 0.35 ? 1.15 * c.Personality.EnergyBias * activityBoost : 0.0),
            (() => StartHandstand(c), c.PreviousDecisionState != StickmanState.Handstand && c.Energy > 0.35 ? 0.9 * c.Personality.EnergyBias * c.Personality.PlayfulnessBias : 0.0),
            (() => StartPushups(c), c.PreviousDecisionState != StickmanState.Pushups && c.Energy > 0.35 ? 0.95 * c.Personality.EnergyBias : 0.0),
            (() => TryJumpToAnotherSurface(c, snapshot, dpi), c.JumpCooldown <= 0 ? 1.9 * c.Personality.EnergyBias * activityBoost : 0.0),
            (() => TryStartClimbingNearbyWall(c, snapshot, dpi), c.ClimbCooldown <= 0 ? 1.6 * c.Personality.CuriosityBias * activityBoost : 0.0),
            (() => TryPeekNearEdgeOrFriend(c, characters, snapshot, dpi), 0.85 * c.Personality.PlayfulnessBias),
            (() => TryInvestigateCursor(c, input, settings, dpi), settings.EnableMouseInteraction ? 0.95 * c.Personality.CuriosityBias : 0.0)
        };

        // Try weighted picks up to a few attempts until one succeeds.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            double total = weights.Sum(w => Math.Max(0, w.Weight));
            if (total <= 0.001)
            {
                break;
            }

            double roll = _rng.NextDouble() * total;
            double acc = 0;
            foreach (var (action, weight) in weights)
            {
                acc += Math.Max(0, weight);
                if (roll <= acc)
                {
                    if (action())
                    {
                        c.PreviousDecisionState = c.State;
                        return;
                    }
                    break;
                }
            }
        }

        StartWalk(c, snapshot, false);
    }

    private bool StartWalk(StickmanCharacter c, DesktopEnvironmentSnapshot snapshot, bool run)
    {
        if (c.GroundedSurface is null)
        {
            return false;
        }

        double dpi = c.DpiScale;
        double span = c.GroundedSurface.Length;
        if (span < 35.0 * dpi)
        {
            return false;
        }

        double minX = c.GroundedSurface.Left + 18.0 * dpi;
        double maxX = c.GroundedSurface.Right - 18.0 * dpi;
        if (maxX <= minX)
        {
            minX = c.GroundedSurface.Left;
            maxX = c.GroundedSurface.Right;
        }

        double destX = minX + _rng.NextDouble() * (maxX - minX);
        if (Math.Abs(destX - c.X) < 35.0 * dpi)
        {
            destX = c.X < (minX + maxX) * 0.5 ? maxX : minX;
        }

        c.TargetSurface = null;
        c.MoveTargetX = destX;
        c.Facing = destX >= c.X ? 1 : -1;
        c.State = run ? StickmanState.Run : StickmanState.Walk;
        c.StateTime = 0;
        c.StateDuration = run ? 1.6 + _rng.NextDouble() * 2.0 : 2.2 + _rng.NextDouble() * 3.0;
        return true;
    }

    private bool StartIdleOrLook(StickmanCharacter c)
    {
        c.Vx = 0;
        c.MoveTargetX = null;
        c.State = _rng.NextDouble() < 0.5 ? StickmanState.LookAround : StickmanState.Idle;
        c.StateTime = 0;
        c.StateDuration = 1.2 + _rng.NextDouble() * 2.2;
        return true;
    }

    private bool StartSit(StickmanCharacter c)
    {
        if (c.GroundedSurface is null)
        {
            return false;
        }

        c.Vx = 0;
        c.MoveTargetX = null;
        c.State = StickmanState.Sit;
        c.StateTime = 0;
        c.StateDuration = 3.0 + _rng.NextDouble() * 5.0 * (0.5 + c.Personality.LazinessBias);
        return true;
    }

    private bool StartSleep(StickmanCharacter c)
    {
        if (c.GroundedSurface is null || c.GroundedSurface.Length < 48.0 * c.DpiScale)
        {
            return false;
        }

        c.Vx = 0;
        c.MoveTargetX = null;
        c.State = StickmanState.Sleep;
        c.StateTime = 0;
        c.StateDuration = 5.5 + _rng.NextDouble() * 6.0;
        c.SleepCooldown = 14.0;
        return true;
    }

    private bool StartWave(StickmanCharacter c)
    {
        c.Vx = 0;
        c.MoveTargetX = null;
        c.State = StickmanState.Wave;
        c.StateTime = 0;
        c.StateDuration = 1.3 + _rng.NextDouble() * 0.8;
        return true;
    }

    private bool TryPeekNearEdgeOrFriend(
        StickmanCharacter c,
        IReadOnlyList<StickmanCharacter> characters,
        DesktopEnvironmentSnapshot snapshot,
        double dpi)
    {
        if (c.GroundedSurface is null)
        {
            return false;
        }

        var nearbyFriend = characters
            .Where(o => o.Id != c.Id && o.IsGrounded && Math.Abs(o.Y - c.Y) < 20.0 * dpi && Math.Abs(o.X - c.X) < 180.0 * dpi)
            .OrderBy(o => Math.Abs(o.X - c.X))
            .FirstOrDefault();

        if (nearbyFriend is not null)
        {
            double behindX = Math.Clamp(
                nearbyFriend.X - nearbyFriend.Facing * 11.0 * dpi,
                c.GroundedSurface.Left + 12.0 * dpi,
                c.GroundedSurface.Right - 12.0 * dpi);

            if (Math.Abs(behindX - c.X) > 20.0 * dpi)
            {
                c.MoveTargetX = behindX;
                c.Facing = behindX >= c.X ? 1 : -1;
                c.State = StickmanState.Walk;
                c.StateTime = 0;
                c.StateDuration = 2.0;
                return true;
            }

            c.X = behindX;
            c.Facing = nearbyFriend.Facing;
        }

        c.Vx = 0;
        c.MoveTargetX = null;
        c.State = StickmanState.Peek;
        c.StateTime = 0;
        c.StateDuration = 1.9 + _rng.NextDouble() * 1.4;
        return true;
    }

    private bool StartStretch(StickmanCharacter c)
    {
        c.Vx = 0;
        c.MoveTargetX = null;
        c.State = StickmanState.Stretch;
        c.StateTime = 0;
        c.StateDuration = 1.9 + _rng.NextDouble() * 0.9;
        return true;
    }

    private bool StartThink(StickmanCharacter c)
    {
        c.Vx = 0;
        c.MoveTargetX = null;
        c.State = StickmanState.Think;
        c.StateTime = 0;
        c.StateDuration = 2.1 + _rng.NextDouble() * 1.4;
        if (_rng.NextDouble() < 0.38)
        {
            c.TriggerEmote("Hmm?", 1.1);
        }
        return true;
    }

    private bool StartDance(StickmanCharacter c)
    {
        c.Vx = 0;
        c.MoveTargetX = null;
        c.State = StickmanState.Dance;
        c.StateTime = 0;
        c.StateDuration = 2.8 + _rng.NextDouble() * 1.6;
        if (_rng.NextDouble() < 0.5)
        {
            c.TriggerEmote("♪", 1.3);
        }
        return true;
    }

    private bool TryBackflip(StickmanCharacter c)
    {
        if (!c.IsGrounded || c.GroundedSurface is null || c.JumpCooldown > 0)
        {
            return false;
        }

        LaunchBackflip(c);
        return true;
    }

    private bool StartHandstand(StickmanCharacter c)
    {
        c.Vx = 0;
        c.MoveTargetX = null;
        c.State = StickmanState.Handstand;
        c.StateTime = 0;
        c.StateDuration = 2.2 + _rng.NextDouble() * 1.1;
        return true;
    }

    private bool StartPushups(StickmanCharacter c)
    {
        c.Vx = 0;
        c.MoveTargetX = null;
        c.State = StickmanState.Pushups;
        c.StateTime = 0;
        c.StateDuration = 2.6 + _rng.NextDouble() * 1.3;
        return true;
    }

    private bool TryTriggerProximitySocialInteraction(
        StickmanCharacter c,
        IReadOnlyList<StickmanCharacter> characters,
        List<ParticleEffect> particles,
        AppSettings settings,
        double dt)
    {
        double dpi = c.DpiScale;
        StickmanCharacter? friend = null;
        double bestDist = double.MaxValue;

        foreach (var other in characters)
        {
            if (other.Id == c.Id ||
                other.IsGrabbed ||
                !other.IsGrounded ||
                other.PartnerCharacterId.HasValue ||
                other.SocialCooldown > 0 ||
                _eventDirector.IsParticipating(other.Id))
            {
                continue;
            }

            if (other.State is not (StickmanState.Idle or StickmanState.LookAround or StickmanState.Walk or StickmanState.Sit or StickmanState.Think or StickmanState.Stretch))
            {
                continue;
            }

            if (Math.Abs(other.Y - c.Y) > 18.0 * dpi)
            {
                continue;
            }

            double dist = Math.Abs(other.X - c.X);
            if (dist >= 10.0 * dpi && dist <= 64.0 * dpi && dist < bestDist)
            {
                bestDist = dist;
                friend = other;
            }
        }

        if (friend is null)
        {
            return false;
        }

        double activityScale = settings.ActivityLevel switch
        {
            ActivityLevel.Calm => 0.45,
            ActivityLevel.Lively => 1.75,
            _ => 1.0
        };

        double chance = Math.Clamp(dt * 3.8 * activityScale * c.Personality.PlayfulnessBias, 0.04, 0.65);
        if (_rng.NextDouble() > chance)
        {
            return false;
        }

        double cooldown = 5.5 / activityScale;
        c.SocialCooldown = cooldown;
        friend.SocialCooldown = cooldown;
        c.Vx = 0;
        friend.Vx = 0;
        c.MoveTargetX = null;
        friend.MoveTargetX = null;
        c.TargetSurface = null;
        friend.TargetSurface = null;
        c.Facing = friend.X >= c.X ? 1 : -1;
        friend.Facing = -c.Facing;
        c.PartnerCharacterId = friend.Id;
        friend.PartnerCharacterId = c.Id;
        c.LookTargetCharacterId = friend.Id;
        friend.LookTargetCharacterId = c.Id;

        double midX = (c.X + friend.X) * 0.5;

        // Spontaneous playful shove / quick spar when fighting is enabled.
        if (settings.EnableFighting &&
            c.FightCooldown <= 0 &&
            friend.FightCooldown <= 0 &&
            bestDist <= 52.0 * dpi &&
            _rng.NextDouble() < 0.25 + 0.35 * c.Personality.AggressionBias)
        {
            c.CombatMove = (CombatMoveKind)_rng.Next(4); // Punch, ComboPunch, Uppercut, or Kick
            c.State = StickmanState.FightAttack;
            c.StateTime = 0;
            c.StateDuration = 0.44;
            c.FightCooldown = cooldown;
            friend.FightCooldown = cooldown;

            double reactRoll = _rng.NextDouble();
            if (reactRoll < 0.45)
            {
                friend.State = StickmanState.Block;
                friend.StateTime = 0;
                friend.StateDuration = 0.46;
                friend.TriggerEmote("!", 0.6);
                PhysicsEngine.SpawnBlockSpark(particles, midX, c.Y - 30.0 * dpi, dpi);
            }
            else if (reactRoll < 0.75)
            {
                friend.State = StickmanState.FightDodge;
                friend.StateTime = 0;
                friend.StateDuration = 0.42;
                friend.Vx = -friend.Facing * 65.0 * dpi;
                friend.TriggerEmote("Haha", 0.7);
            }
            else
            {
                friend.State = StickmanState.HitReact;
                friend.StateTime = 0;
                friend.StateDuration = 0.38;
                friend.Vx = c.Facing * 95.0 * dpi;
                PhysicsEngine.SpawnHitSpark(particles, midX, c.Y - 30.0 * dpi, dpi, c.PrimaryColor);
            }

            return true;
        }

        // Social gestures: HighFive, Chat, or SitTogether.
        double socialRoll = _rng.NextDouble();
        if (socialRoll < 0.40 && bestDist <= 54.0 * dpi && c.State != StickmanState.Sit && friend.State != StickmanState.Sit)
        {
            c.State = StickmanState.HighFive;
            friend.State = StickmanState.HighFive;
            c.StateTime = 0;
            friend.StateTime = 0;
            c.StateDuration = 0.72;
            friend.StateDuration = 0.72;
            PhysicsEngine.SpawnSocialBurst(particles, midX, c.Y - 48.0 * dpi, dpi, "High 5!", c.PrimaryColor);
        }
        else if (socialRoll < 0.76)
        {
            double dur = 2.6 + _rng.NextDouble() * 1.4;
            c.State = StickmanState.Chat;
            friend.State = StickmanState.Chat;
            c.StateTime = 0;
            friend.StateTime = 0;
            c.StateDuration = dur;
            friend.StateDuration = dur;
            c.TriggerEmote(_rng.Next(2) == 0 ? "..." : "?!", 1.2);
            friend.TriggerEmote(_rng.Next(2) == 0 ? "Haha" : "!", 1.2);
        }
        else
        {
            double dur = 4.2 + _rng.NextDouble() * 2.2;
            c.State = StickmanState.SitTogether;
            friend.State = StickmanState.SitTogether;
            c.StateTime = 0;
            friend.StateTime = 0;
            c.StateDuration = dur;
            friend.StateDuration = dur;
        }

        return true;
    }

    private void UpdateSocialAndSpectatorAwareness(
        StickmanCharacter c,
        IReadOnlyList<StickmanCharacter> characters,
        InputInteractionManager input,
        AppSettings settings,
        double dt)
    {
        if (c.State == StickmanState.Sleep)
        {
            return;
        }

        double dpi = c.DpiScale;
        bool cursorClose = settings.EnableMouseInteraction && input.HasCursor &&
            Math.Sqrt(
                Math.Pow(input.CursorX - c.X, 2) +
                Math.Pow(input.CursorY - (c.Y - 40.0 * c.VisualScale * dpi), 2)) < 160.0 * dpi;

        // Track partner or look target with eyes when cursor isn't demanding attention.
        if (!cursorClose)
        {
            int? lookId = c.LookTargetCharacterId ?? c.PartnerCharacterId;
            if (lookId.HasValue)
            {
                var target = characters.FirstOrDefault(x => x.Id == lookId.Value);
                if (target is not null && target.Id != c.Id)
                {
                    double dx = target.X - c.X;
                    double dy = (target.Y - 38.0 * target.VisualScale * target.DpiScale) - (c.Y - 38.0 * c.VisualScale * dpi);
                    Vec2 desired = new Vec2(dx, dy).Normalized() * 1.4;
                    c.EyeLookOffset = Vec2.Lerp(c.EyeLookOffset, desired, Math.Clamp(dt * 12.0, 0, 1));
                }
            }
        }

        // React naturally when user grabs a friend or a friend falls fast nearby.
        if (_eventDirector.ActiveEvent == MiniEventKind.None &&
            c.IsGrounded &&
            !c.PartnerCharacterId.HasValue &&
            c.SocialCooldown <= 0 &&
            c.State is StickmanState.Idle or StickmanState.LookAround or StickmanState.Walk or StickmanState.Sit or StickmanState.Think)
        {
            var dramatic = characters.FirstOrDefault(o =>
                o.Id != c.Id &&
                (o.IsGrabbed || (o.State == StickmanState.Fall && o.Vy > 220.0 * o.DpiScale)) &&
                Math.Abs(o.X - c.X) <= 320.0 * dpi);

            if (dramatic is not null)
            {
                c.Facing = dramatic.X >= c.X ? 1 : -1;
                c.LookTargetCharacterId = dramatic.Id;
                c.Vx = 0;
                c.MoveTargetX = null;
                c.State = StickmanState.Cheer;
                c.StateTime = 0;
                c.StateDuration = 1.05;
                c.SocialCooldown = 4.5;
                c.TriggerEmote(dramatic.IsGrabbed ? "Whoa!" : "!", 0.9);
            }
        }
    }

    private bool TryInvestigateCursor(
        StickmanCharacter c,
        InputInteractionManager input,
        AppSettings settings,
        double dpi)
    {
        if (!settings.EnableMouseInteraction || !input.HasCursor || c.GroundedSurface is null)
        {
            return false;
        }

        if (Math.Abs(input.CursorY - c.Y) > 160 * dpi || Math.Abs(input.CursorX - c.X) > 360 * dpi)
        {
            return false;
        }

        double clampedTarget = Math.Clamp(input.CursorX, c.GroundedSurface.Left + 16 * dpi, c.GroundedSurface.Right - 16 * dpi);
        c.MoveTargetX = clampedTarget;
        c.Facing = clampedTarget >= c.X ? 1 : -1;
        c.State = StickmanState.Walk;
        c.StateTime = 0;
        c.StateDuration = 2.5;
        return true;
    }

    private bool TryJumpToAnotherSurface(StickmanCharacter c, DesktopEnvironmentSnapshot snapshot, double dpi)
    {
        var reachable = snapshot.WalkableSurfaces
            .Where(s => s != c.GroundedSurface && s.Length >= 40 * dpi)
            .Select(s =>
            {
                double targetX = Math.Clamp(c.X, s.Left + 18 * dpi, s.Right - 18 * dpi);
                double dx = Math.Abs(targetX - c.X);
                double dy = s.Y - c.Y;
                return (Surface: s, TargetX: targetX, Dx: dx, Dy: dy);
            })
            .Where(x => x.Dx < 260 * dpi && x.Dy > -190 * dpi && x.Dy < 340 * dpi)
            .ToList();

        if (reachable.Count == 0)
        {
            // Small celebratory hop in place or along current surface.
            if (c.GroundedSurface is not null)
            {
                c.Vy = -420.0 * dpi;
                c.Vx = c.Facing * 95.0 * dpi;
                c.IsGrounded = false;
                c.GroundedSurface = null;
                c.FallStartY = c.Y;
                c.State = StickmanState.Jump;
                c.StateTime = 0;
                c.StateDuration = 1.0;
                c.JumpCooldown = 2.5;
                return true;
            }
            return false;
        }

        var chosen = reachable[_rng.Next(reachable.Count)];
        LaunchBallisticJump(c, chosen.TargetX, chosen.Surface.Y, dpi);
        return true;
    }

    private bool TryJumpToAdjacentSurface(StickmanCharacter c, DesktopEnvironmentSnapshot snapshot, double dpi)
    {
        var next = snapshot.WalkableSurfaces
            .Where(s => s != c.GroundedSurface && s.Length >= 40 * dpi)
            .FirstOrDefault(s =>
            {
                double targetX = Math.Clamp(c.X + c.Facing * 90 * dpi, s.Left + 12 * dpi, s.Right - 12 * dpi);
                double dx = (targetX - c.X) * c.Facing;
                double dy = s.Y - c.Y;
                return dx > 10 * dpi && dx < 220 * dpi && dy > -150 * dpi && dy < 220 * dpi;
            });

        if (next is null)
        {
            return false;
        }

        double tx = Math.Clamp(c.X + c.Facing * 80 * dpi, next.Left + 16 * dpi, next.Right - 16 * dpi);
        LaunchBallisticJump(c, tx, next.Y, dpi);
        return true;
    }

    private void TryTraverseOrJumpToward(
        StickmanCharacter c,
        double targetX,
        double targetY,
        DesktopEnvironmentSnapshot snapshot,
        double dpi)
    {
        if (c.GroundedSurface is null || c.JumpCooldown > 0)
        {
            return;
        }

        // If target is significantly higher and there's a surface closer to it, try jumping onto it!
        if (targetY < c.Y - 45 * dpi)
        {
            var stepUp = snapshot.WalkableSurfaces
                .Where(s => s != c.GroundedSurface && s.Y < c.Y - 25 * dpi && s.Y >= c.Y - 190 * dpi)
                .OrderBy(s => Math.Abs(Math.Clamp(targetX, s.Left, s.Right) - c.X))
                .FirstOrDefault();

            if (stepUp is not null)
            {
                double tx = Math.Clamp(targetX, stepUp.Left + 16 * dpi, stepUp.Right - 16 * dpi);
                if (Math.Abs(tx - c.X) < 220 * dpi)
                {
                    LaunchBallisticJump(c, tx, stepUp.Y, dpi);
                }
            }
        }
    }

    private void TryNavigateToTargetSurface(StickmanCharacter c, DesktopEnvironmentSnapshot snapshot, double dpi)
    {
        if (c.TargetSurface is null)
        {
            return;
        }

        // Check if we can climb a wall belonging to TargetSurface.
        var wall = snapshot.ClimbableWalls
            .Where(w => w.SourceHwnd == c.TargetSurface.SourceHwnd)
            .OrderBy(w => Math.Abs(w.X - c.X))
            .FirstOrDefault();

        if (wall is not null && Math.Abs(wall.X - c.X) < 28 * dpi && c.Y >= wall.TopY + 30 * dpi && c.Y <= wall.BottomY + 25 * dpi)
        {
            BeginClimb(c, wall, dpi);
            c.TargetSurface = null;
            return;
        }

        // Or jump directly if close enough.
        double tx = Math.Clamp(c.X, c.TargetSurface.Left + 16 * dpi, c.TargetSurface.Right - 16 * dpi);
        double dy = c.TargetSurface.Y - c.Y;
        if (Math.Abs(tx - c.X) < 200 * dpi && dy > -185 * dpi && dy < 220 * dpi && c.JumpCooldown <= 0)
        {
            LaunchBallisticJump(c, tx, c.TargetSurface.Y, dpi);
            c.TargetSurface = null;
        }
    }

    private bool TryStartClimbingNearbyWall(StickmanCharacter c, DesktopEnvironmentSnapshot snapshot, double dpi)
    {
        if (c.ClimbCooldown > 0)
        {
            return false;
        }

        var wall = snapshot.ClimbableWalls
            .Where(w =>
                Math.Abs(w.X - c.X) <= 32.0 * dpi &&
                c.Y >= w.TopY + 42.0 * dpi &&
                c.Y <= w.BottomY + 28.0 * dpi)
            .OrderBy(w => Math.Abs(w.X - c.X))
            .FirstOrDefault();

        if (wall is null)
        {
            return false;
        }

        BeginClimb(c, wall, dpi);
        return true;
    }

    private static void BeginClimb(StickmanCharacter c, ClimbableWall wall, double dpi)
    {
        c.IsGrounded = false;
        c.GroundedSurface = null;
        c.ClimbingWall = wall;
        c.X = wall.X;
        c.Y = Math.Clamp(c.Y, wall.TopY + 24 * dpi, wall.BottomY - 4 * dpi);
        c.Vx = 0;
        c.Vy = 0;
        c.Facing = wall.Side == WallSide.Left ? 1 : -1;
        c.State = StickmanState.Climb;
        c.StateTime = 0;
        c.StateDuration = 6.0;
        c.MoveTargetX = null;
    }

    public static void LaunchBallisticJump(StickmanCharacter c, double targetX, double targetY, double dpi)
    {
        double gravity = PhysicsEngine.GravityDipPerSecSq * dpi;
        double apexY = Math.Min(c.Y, targetY) - 44.0 * dpi;

        double riseHeight = Math.Max(16.0 * dpi, c.Y - apexY);
        double fallHeight = Math.Max(16.0 * dpi, targetY - apexY);

        double vy = -Math.Sqrt(2.0 * gravity * riseHeight);
        double tUp = -vy / gravity;
        double tDown = Math.Sqrt((2.0 * fallHeight) / gravity);
        double totalFlight = Math.Max(0.22, tUp + tDown);

        double vx = Math.Clamp((targetX - c.X) / totalFlight, -320.0 * dpi, 320.0 * dpi);

        c.Vx = vx;
        c.Vy = vy;
        c.Facing = vx >= 0 ? 1 : -1;
        c.IsGrounded = false;
        c.GroundedSurface = null;
        c.FallStartY = c.Y;
        c.State = StickmanState.Jump;
        c.StateTime = 0;
        c.StateDuration = totalFlight + 0.3;
        c.JumpCooldown = 2.0;
        c.MoveTargetX = null;
    }

    public static void LaunchBackflip(StickmanCharacter c)
    {
        double dpi = c.DpiScale;
        c.IsGrounded = false;
        c.GroundedSurface = null;
        c.ClimbingWall = null;
        c.FallStartY = c.Y;
        c.MoveTargetX = null;
        c.TargetSurface = null;
        c.Vy = -355.0 * dpi;
        c.Vx = -c.Facing * 55.0 * dpi;
        c.State = StickmanState.Backflip;
        c.StateTime = 0;
        c.StateDuration = 0.62;
        c.JumpCooldown = 2.0;
    }

    private static void SpawnSleepZzz(List<ParticleEffect> particles, StickmanCharacter c)
    {
        double dpi = c.DpiScale;
        double s = c.VisualScale;
        particles.Add(new ParticleEffect
        {
            X = c.X + c.Facing * 15.0 * s * dpi,
            Y = c.Y - 14.0 * s * dpi,
            Vx = c.Facing * 10.0 * dpi,
            Vy = -26.0 * dpi,
            Life = 0,
            MaxLife = 1.6,
            Size = 11.0 * s * dpi,
            Color = Color.FromArgb(220, 235, 244, 255),
            Text = "Z"
        });
    }
}
