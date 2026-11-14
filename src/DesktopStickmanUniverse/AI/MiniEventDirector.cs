using System.Windows.Media;
using DesktopStickmanUniverse.Desktop;
using DesktopStickmanUniverse.Physics;
using DesktopStickmanUniverse.Settings;

namespace DesktopStickmanUniverse.AI;

public enum MiniEventKind
{
    None,
    SpectatedDuel,
    TagChase,
    GatheringChat,
    DanceParty,
    AcrobaticsShow,
    SummitClimb
}

/// <summary>
/// Coordinates emergent multi-character desktop events: dynamic multi-round duels with spectators,
/// dance parties, acrobatics shows, social chats, tag chases, and window summit climbs.
/// </summary>
public sealed class MiniEventDirector
{
    private readonly Random _rng;
    private double _globalCooldown = 3.5;
    private double _fightPairCooldown = 5.0;
    private double _activeEventTimer;
    private double _subPhaseTimer;
    private MiniEventKind _activeEvent = MiniEventKind.None;

    private int? _primaryId;
    private int? _secondaryId;
    private readonly List<int> _groupIds = new();
    private readonly List<int> _spectatorIds = new();
    private double _gatheringX;
    private int _sparRoundsRemaining;
    private bool _duelFinaleTriggered;
    private bool _gatheringSparkedFight;

    public MiniEventKind ActiveEvent => _activeEvent;

    public bool IsParticipating(int characterId) =>
        _activeEvent != MiniEventKind.None &&
        (characterId == _primaryId ||
         characterId == _secondaryId ||
         _groupIds.Contains(characterId) ||
         _spectatorIds.Contains(characterId));

    public MiniEventDirector(Random? rng = null)
    {
        _rng = rng ?? new Random();
    }

    public void Reset()
    {
        _activeEvent = MiniEventKind.None;
        _activeEventTimer = 0;
        _subPhaseTimer = 0;
        _primaryId = null;
        _secondaryId = null;
        _groupIds.Clear();
        _spectatorIds.Clear();
        _globalCooldown = 3.0;
    }

    public void Update(
        IReadOnlyList<StickmanCharacter> characters,
        List<ParticleEffect> particles,
        DesktopEnvironmentSnapshot snapshot,
        AppSettings settings,
        double dt)
    {
        _globalCooldown = Math.Max(0, _globalCooldown - dt);
        _fightPairCooldown = Math.Max(0, _fightPairCooldown - dt);

        if (characters.Count < 2)
        {
            if (_activeEvent != MiniEventKind.None)
            {
                EndActiveEvent(characters);
            }
            return;
        }

        if (_activeEvent != MiniEventKind.None)
        {
            _activeEventTimer -= dt;
            _subPhaseTimer += dt;
            StepActiveEvent(characters, particles, snapshot, settings, dt);
            if (_activeEventTimer <= 0)
            {
                EndActiveEvent(characters);
            }
            return;
        }

        if (_globalCooldown > 0)
        {
            return;
        }

        double triggerChancePerSec = settings.ActivityLevel switch
        {
            ActivityLevel.Calm => 0.16,
            ActivityLevel.Lively => 0.58,
            _ => 0.34
        };

        if (_rng.NextDouble() > triggerChancePerSec * dt)
        {
            return;
        }

        TryStartRandomEvent(characters, snapshot, settings);
    }

    public bool TryStartDuel(
        IReadOnlyList<StickmanCharacter> characters,
        StickmanCharacter challenger,
        AppSettings settings)
    {
        if (!settings.EnableFighting || _activeEvent != MiniEventKind.None)
        {
            return false;
        }

        var opponent = characters
            .Where(c => c.Id != challenger.Id && !c.IsGrabbed && c.State != StickmanState.Sleep)
            .OrderBy(c => Math.Abs(c.Y - challenger.Y) + Math.Abs(c.X - challenger.X) * 0.3)
            .FirstOrDefault();

        if (opponent is null)
        {
            return false;
        }

        BeginSpectatedDuel(characters, challenger, opponent, settings);
        return true;
    }

    private void TryStartRandomEvent(
        IReadOnlyList<StickmanCharacter> characters,
        DesktopEnvironmentSnapshot snapshot,
        AppSettings settings)
    {
        var available = characters
            .Where(c => !c.IsGrabbed && c.IsGrounded && c.State != StickmanState.Sleep && c.MiniEventCooldown <= 0 && c.PartnerCharacterId is null)
            .ToList();

        if (available.Count < 2)
        {
            return;
        }

        var candidates = new List<MiniEventKind>();
        if (settings.EnableFighting && _fightPairCooldown <= 0)
        {
            candidates.Add(MiniEventKind.SpectatedDuel);
            candidates.Add(MiniEventKind.SpectatedDuel); // Favor dynamic duels naturally when fighting is enabled.
        }

        candidates.Add(MiniEventKind.TagChase);
        candidates.Add(MiniEventKind.DanceParty);
        candidates.Add(MiniEventKind.AcrobaticsShow);
        candidates.Add(MiniEventKind.GatheringChat);

        if (snapshot.WalkableSurfaces.Any(s => s.Kind == SurfaceKind.WindowTop))
        {
            candidates.Add(MiniEventKind.SummitClimb);
        }

        MiniEventKind chosen = candidates[_rng.Next(candidates.Count)];
        switch (chosen)
        {
            case MiniEventKind.SpectatedDuel:
            {
                var a = available.OrderByDescending(c => c.Personality.AggressionBias + _rng.NextDouble() * 0.45).First();
                var b = available
                    .Where(c => c.Id != a.Id)
                    .OrderBy(c => Math.Abs(c.Y - a.Y) + Math.Abs(c.X - a.X) * 0.25)
                    .FirstOrDefault();
                if (b is not null)
                {
                    BeginSpectatedDuel(characters, a, b, settings);
                }
                break;
            }

            case MiniEventKind.TagChase:
                StartTagChase(available);
                break;

            case MiniEventKind.DanceParty:
                StartDanceParty(available, settings);
                break;

            case MiniEventKind.AcrobaticsShow:
                StartAcrobaticsShow(available, settings);
                break;

            case MiniEventKind.GatheringChat:
                StartGatheringChat(available, settings);
                break;

            case MiniEventKind.SummitClimb:
                StartSummitClimb(available, snapshot, settings);
                break;
        }
    }

    private void BeginSpectatedDuel(
        IReadOnlyList<StickmanCharacter> allCharacters,
        StickmanCharacter a,
        StickmanCharacter b,
        AppSettings settings)
    {
        _activeEvent = MiniEventKind.SpectatedDuel;
        _activeEventTimer = 7.2;
        _subPhaseTimer = 0;
        _sparRoundsRemaining = _rng.Next(4, 8);
        _duelFinaleTriggered = false;
        _primaryId = a.Id;
        _secondaryId = b.Id;
        _groupIds.Clear();
        _spectatorIds.Clear();

        a.PartnerCharacterId = b.Id;
        b.PartnerCharacterId = a.Id;
        a.MoveTargetX = null;
        b.MoveTargetX = null;
        a.FollowCursorMode = false;
        b.FollowCursorMode = false;
        a.TriggerEmote("!", 0.75);
        b.TriggerEmote("!", 0.75);

        // If fighters are on different vertical levels, have one leap toward the other's surface.
        if (Math.Abs(a.Y - b.Y) > 35.0 * a.DpiScale && a.IsGrounded && b.GroundedSurface is not null)
        {
            double tx = Math.Clamp(b.X + (a.X < b.X ? -40 : 40) * a.DpiScale, b.GroundedSurface.Left + 16, b.GroundedSurface.Right - 16);
            if (Math.Abs(tx - a.X) < 340 * a.DpiScale)
            {
                AiBehaviorController.LaunchBallisticJump(a, tx, b.GroundedSurface.Y, a.DpiScale);
            }
        }

        // Invite up to 2 nearby idle/walking characters to spectate & cheer!
        double midX = (a.X + b.X) * 0.5;
        var bystanders = allCharacters
            .Where(c => c.Id != a.Id && c.Id != b.Id && !c.IsGrabbed && c.IsGrounded && c.State != StickmanState.Sleep && c.PartnerCharacterId is null)
            .OrderBy(c => Math.Abs(c.X - midX) + Math.Abs(c.Y - a.Y))
            .Take(2);

        foreach (var spec in bystanders)
        {
            if (Math.Abs(spec.X - midX) < 340 * spec.DpiScale)
            {
                _spectatorIds.Add(spec.Id);
                spec.PartnerCharacterId = a.Id;
                spec.LookTargetCharacterId = a.Id;
                spec.Vx = 0;
                spec.MoveTargetX = null;
                spec.Facing = midX >= spec.X ? 1 : -1;
                spec.State = StickmanState.Cheer;
                spec.StateTime = 0;
                spec.StateDuration = 6.5;
                spec.TriggerEmote("Go!", 1.0);
            }
        }

        _globalCooldown = GetGlobalCooldown(settings);
        _fightPairCooldown = settings.ActivityLevel == ActivityLevel.Lively ? 6.5 : 10.5;
    }

    private void StartTagChase(List<StickmanCharacter> available)
    {
        var chaser = available[_rng.Next(available.Count)];
        var runner = available.Where(c => c.Id != chaser.Id).OrderBy(c => Math.Abs(c.X - chaser.X)).FirstOrDefault();
        if (runner is null)
        {
            return;
        }

        _activeEvent = MiniEventKind.TagChase;
        _activeEventTimer = 5.5;
        _subPhaseTimer = 0;
        _primaryId = chaser.Id;
        _secondaryId = runner.Id;
        _groupIds.Clear();
        _spectatorIds.Clear();

        chaser.PartnerCharacterId = runner.Id;
        runner.PartnerCharacterId = chaser.Id;
        chaser.State = StickmanState.Run;
        runner.State = StickmanState.Run;
        chaser.TriggerEmote("!", 0.8);
        _globalCooldown = 7.0;
    }

    private void StartDanceParty(List<StickmanCharacter> available, AppSettings settings)
    {
        var anchor = available[_rng.Next(available.Count)];
        if (anchor.GroundedSurface is null)
        {
            return;
        }

        _activeEvent = MiniEventKind.DanceParty;
        _activeEventTimer = 5.4;
        _subPhaseTimer = 0;
        _gatheringX = Math.Clamp(anchor.X, anchor.GroundedSurface.Left + 55, anchor.GroundedSurface.Right - 55);
        _groupIds.Clear();
        _spectatorIds.Clear();

        var dancers = available.Take(4).ToList();
        for (int i = 0; i < dancers.Count; i++)
        {
            var c = dancers[i];
            _groupIds.Add(c.Id);
            c.PartnerCharacterId = anchor.Id;
            double offset = (i - (dancers.Count - 1) * 0.5) * 28.0 * c.DpiScale;
            c.MoveTargetX = Math.Clamp(_gatheringX + offset, anchor.GroundedSurface.Left + 16, anchor.GroundedSurface.Right - 16);
            c.TargetSurface = anchor.GroundedSurface;
            c.State = StickmanState.Walk;
            c.StateTime = 0;
            c.StateDuration = 2.5;
        }

        _globalCooldown = GetGlobalCooldown(settings);
    }

    private void StartAcrobaticsShow(List<StickmanCharacter> available, AppSettings settings)
    {
        var performer = available.OrderByDescending(c => c.Personality.EnergyBias + _rng.NextDouble() * 0.3).First();
        if (performer.GroundedSurface is null)
        {
            return;
        }

        _activeEvent = MiniEventKind.AcrobaticsShow;
        _activeEventTimer = 4.6;
        _subPhaseTimer = 0;
        _primaryId = performer.Id;
        _secondaryId = null;
        _groupIds.Clear();
        _spectatorIds.Clear();

        performer.PartnerCharacterId = performer.Id;
        performer.Vx = 0;
        performer.MoveTargetX = null;

        int trickRoll = _rng.Next(3);
        if (trickRoll == 0)
        {
            AiBehaviorController.LaunchBackflip(performer);
        }
        else if (trickRoll == 1)
        {
            performer.State = StickmanState.Handstand;
            performer.StateTime = 0;
            performer.StateDuration = 3.2;
        }
        else
        {
            performer.State = StickmanState.Pushups;
            performer.StateTime = 0;
            performer.StateDuration = 3.4;
        }

        foreach (var spec in available.Where(c => c.Id != performer.Id).Take(3))
        {
            _spectatorIds.Add(spec.Id);
            spec.PartnerCharacterId = performer.Id;
            spec.LookTargetCharacterId = performer.Id;
            spec.Vx = 0;
            spec.Facing = performer.X >= spec.X ? 1 : -1;
            spec.State = StickmanState.Cheer;
            spec.StateTime = 0;
            spec.StateDuration = 4.2;
            spec.TriggerEmote("!", 0.9);
        }

        _globalCooldown = GetGlobalCooldown(settings);
    }

    private void StartGatheringChat(List<StickmanCharacter> available, AppSettings settings)
    {
        var anchor = available[_rng.Next(available.Count)];
        if (anchor.GroundedSurface is null)
        {
            return;
        }

        _activeEvent = MiniEventKind.GatheringChat;
        _activeEventTimer = 5.0;
        _subPhaseTimer = 0;
        _gatheringSparkedFight = false;
        _gatheringX = Math.Clamp(anchor.X, anchor.GroundedSurface.Left + 55, anchor.GroundedSurface.Right - 55);
        _groupIds.Clear();
        _spectatorIds.Clear();

        var group = available.Take(Math.Min(4, available.Count)).ToList();
        for (int i = 0; i < group.Count; i++)
        {
            var c = group[i];
            _groupIds.Add(c.Id);
            c.PartnerCharacterId = anchor.Id;
            double offset = (i - (group.Count - 1) * 0.5) * 28.0 * c.DpiScale;
            c.MoveTargetX = Math.Clamp(_gatheringX + offset, anchor.GroundedSurface.Left + 16, anchor.GroundedSurface.Right - 16);
            c.TargetSurface = anchor.GroundedSurface;
            c.State = StickmanState.Walk;
            c.StateTime = 0;
            c.StateDuration = 2.6;
        }

        _globalCooldown = GetGlobalCooldown(settings);
    }

    private void StartSummitClimb(List<StickmanCharacter> available, DesktopEnvironmentSnapshot snapshot, AppSettings settings)
    {
        var highest = snapshot.WalkableSurfaces
            .Where(s => s.Kind == SurfaceKind.WindowTop && s.Length >= 60 * s.DpiScale)
            .OrderBy(s => s.Y)
            .FirstOrDefault();

        if (highest is null)
        {
            return;
        }

        var climber = available.OrderByDescending(c => c.Personality.CuriosityBias).First();
        _activeEvent = MiniEventKind.SummitClimb;
        _activeEventTimer = 4.8;
        _subPhaseTimer = 0;
        _primaryId = climber.Id;
        _groupIds.Clear();
        _spectatorIds.Clear();

        climber.TargetSurface = highest;
        climber.MoveTargetX = (highest.Left + highest.Right) * 0.5;
        climber.State = StickmanState.Run;
        climber.StateTime = 0;
        climber.StateDuration = 4.2;
        climber.TriggerEmote("?", 0.8);
        _globalCooldown = GetGlobalCooldown(settings);
    }

    private void StepActiveEvent(
        IReadOnlyList<StickmanCharacter> characters,
        List<ParticleEffect> particles,
        DesktopEnvironmentSnapshot snapshot,
        AppSettings settings,
        double dt)
    {
        switch (_activeEvent)
        {
            case MiniEventKind.SpectatedDuel:
                StepSpectatedDuel(characters, particles, dt);
                break;

            case MiniEventKind.TagChase:
                StepTagChase(characters, snapshot);
                break;

            case MiniEventKind.DanceParty:
                StepDanceParty(characters, particles, settings);
                break;

            case MiniEventKind.AcrobaticsShow:
                StepAcrobaticsShow(characters, particles, settings);
                break;

            case MiniEventKind.GatheringChat:
                StepGatheringChat(characters, settings);
                break;
        }
    }

    private void StepSpectatedDuel(IReadOnlyList<StickmanCharacter> characters, List<ParticleEffect> particles, double dt)
    {
        var a = characters.FirstOrDefault(c => c.Id == _primaryId);
        var b = characters.FirstOrDefault(c => c.Id == _secondaryId);
        if (a is null || b is null || a.IsGrabbed || b.IsGrabbed)
        {
            _activeEventTimer = 0;
            return;
        }

        double dpi = a.DpiScale;
        double dx = b.X - a.X;
        double dist = Math.Abs(dx);

        // Keep spectators oriented toward the midpoint of the duel.
        double midX = (a.X + b.X) * 0.5;
        foreach (int specId in _spectatorIds)
        {
            var spec = characters.FirstOrDefault(c => c.Id == specId);
            if (spec is not null && !spec.IsGrabbed)
            {
                spec.Facing = midX >= spec.X ? 1 : -1;
            }
        }

        if (_duelFinaleTriggered)
        {
            return;
        }

        a.Facing = dx >= 0 ? 1 : -1;
        b.Facing = dx >= 0 ? -1 : 1;

        bool aBusy = a.State is StickmanState.FightAttack or StickmanState.FightDodge or StickmanState.Block or StickmanState.HitReact or StickmanState.Backflip or StickmanState.GetUp;
        bool bBusy = b.State is StickmanState.FightAttack or StickmanState.FightDodge or StickmanState.Block or StickmanState.HitReact or StickmanState.Backflip or StickmanState.GetUp;

        if (aBusy && a.IsGrounded && a.StateTime >= a.StateDuration)
        {
            a.State = StickmanState.Idle;
            a.StateTime = 0;
            a.StateDuration = 0.2;
            a.Vx = 0;
            aBusy = false;
        }

        if (bBusy && b.IsGrounded && b.StateTime >= b.StateDuration)
        {
            b.State = StickmanState.Idle;
            b.StateTime = 0;
            b.StateDuration = 0.2;
            b.Vx = 0;
            bBusy = false;
        }

        if (_sparRoundsRemaining <= 0)
        {
            if (!aBusy && !bBusy && a.IsGrounded && b.IsGrounded)
            {
                _duelFinaleTriggered = true;
                a.Vx = 0;
                b.Vx = 0;
                a.State = dist < 44 * dpi ? StickmanState.HighFive : StickmanState.Wave;
                b.State = dist < 44 * dpi ? StickmanState.HighFive : StickmanState.Wave;
                a.StateTime = 0;
                b.StateTime = 0;
                a.StateDuration = 0.8;
                b.StateDuration = 0.8;
                a.TriggerEmote("GG!", 0.9);
                if (dist < 44 * dpi)
                {
                    PhysicsEngine.SpawnBlockSpark(particles, midX, a.Y - 34 * dpi, dpi);
                }
                _activeEventTimer = Math.Min(_activeEventTimer, 0.85);
            }
            return;
        }

        // Occasional flying kick gap-closer from medium distance.
        if (dist > 45.0 * dpi && dist < 115.0 * dpi && a.IsGrounded && !aBusy && !bBusy && _rng.NextDouble() < 0.8 * dt)
        {
            a.CombatMove = CombatMoveKind.FlyingKick;
            a.State = StickmanState.FightAttack;
            a.StateTime = 0;
            a.StateDuration = 0.55;
            a.Vx = a.Facing * 230.0 * dpi;
            a.Vy = -205.0 * dpi;
            a.IsGrounded = false;
            a.GroundedSurface = null;
            a.FallStartY = a.Y;
            _sparRoundsRemaining--;
            return;
        }

        if (dist > 36.0 * dpi)
        {
            if (a.IsGrounded && !aBusy)
            {
                a.State = StickmanState.Run;
                a.Vx = a.Facing * 155.0 * dpi;
            }
            if (b.IsGrounded && !bBusy)
            {
                b.State = StickmanState.Run;
                b.Vx = b.Facing * 140.0 * dpi;
            }
            return;
        }

        if (!aBusy && !bBusy && a.IsGrounded && b.IsGrounded)
        {
            _sparRoundsRemaining--;
            bool aAttacks = _rng.NextDouble() < 0.52;
            var attacker = aAttacks ? a : b;
            var defender = aAttacks ? b : a;

            CombatMoveKind move = _rng.Next(5) switch
            {
                0 => CombatMoveKind.Punch,
                1 => CombatMoveKind.ComboPunch,
                2 => CombatMoveKind.Uppercut,
                3 => CombatMoveKind.Kick,
                _ => CombatMoveKind.Punch
            };

            attacker.Vx = attacker.Facing * 28.0 * dpi;
            attacker.CombatMove = move;
            attacker.State = StickmanState.FightAttack;
            attacker.StateTime = 0;
            attacker.StateDuration = move == CombatMoveKind.ComboPunch ? 0.52 : 0.36;

            double defenseRoll = _rng.NextDouble();
            if (defenseRoll < 0.28)
            {
                // Solid cross-arm block!
                defender.State = StickmanState.Block;
                defender.StateTime = 0;
                defender.StateDuration = 0.42;
                defender.Vx = attacker.Facing * 45.0 * dpi;
                PhysicsEngine.SpawnBlockSpark(particles, (attacker.X + defender.X) * 0.5, attacker.Y - 30.0 * dpi, dpi);
            }
            else if (defenseRoll < 0.52)
            {
                // Evasive duck or acrobatic backflip dodge!
                if (_rng.NextDouble() < 0.4 && defender.Personality.EnergyBias > 0.6)
                {
                    AiBehaviorController.LaunchBackflip(defender);
                }
                else
                {
                    defender.State = StickmanState.FightDodge;
                    defender.StateTime = 0;
                    defender.StateDuration = 0.40;
                    defender.Vx = -defender.Facing * 110.0 * dpi;
                }
            }
            else
            {
                // Clean playful hit with knockback and impact wave!
                defender.State = StickmanState.HitReact;
                defender.StateTime = 0;
                defender.StateDuration = 0.38;
                bool heavy = move is CombatMoveKind.Uppercut or CombatMoveKind.Kick;
                defender.Vx = attacker.Facing * (heavy ? 210.0 : 145.0) * dpi;
                defender.Vy = (heavy ? -220.0 : -145.0) * dpi;
                defender.IsGrounded = false;
                defender.GroundedSurface = null;
                defender.FallStartY = defender.Y;

                PhysicsEngine.SpawnHitSpark(
                    particles,
                    (attacker.X + defender.X) * 0.5,
                    attacker.Y - 29.0 * dpi,
                    dpi,
                    attacker.PrimaryColor);
            }
        }
    }

    private void StepTagChase(IReadOnlyList<StickmanCharacter> characters, DesktopEnvironmentSnapshot snapshot)
    {
        var chaser = characters.FirstOrDefault(c => c.Id == _primaryId);
        var runner = characters.FirstOrDefault(c => c.Id == _secondaryId);
        if (chaser is null || runner is null || chaser.IsGrabbed || runner.IsGrabbed)
        {
            _activeEventTimer = 0;
            return;
        }

        double dpi = chaser.DpiScale;
        double dx = runner.X - chaser.X;
        chaser.Facing = dx >= 0 ? 1 : -1;

        if (chaser.IsGrounded && chaser.State != StickmanState.Backflip)
        {
            chaser.State = StickmanState.Run;
            chaser.Vx = chaser.Facing * 168.0 * dpi;
        }

        if (runner.IsGrounded && runner.State != StickmanState.Backflip)
        {
            int fleeDir = dx >= 0 ? 1 : -1;
            if (runner.GroundedSurface is not null)
            {
                if (fleeDir > 0 && runner.X > runner.GroundedSurface.Right - 45 * dpi) fleeDir = -1;
                if (fleeDir < 0 && runner.X < runner.GroundedSurface.Left + 45 * dpi) fleeDir = 1;
            }

            runner.Facing = fleeDir;
            runner.State = StickmanState.Run;
            runner.Vx = fleeDir * 178.0 * dpi;

            // Runner occasionally vaults over chaser with an acrobatic backflip if cornered!
            if (Math.Abs(dx) < 35.0 * dpi && _rng.NextDouble() < 0.08 && runner.JumpCooldown <= 0)
            {
                AiBehaviorController.LaunchBackflip(runner);
                runner.Vx = -chaser.Facing * 140.0 * dpi;
                runner.JumpCooldown = 2.5;
            }
        }

        if (Math.Abs(dx) < 20.0 * dpi && Math.Abs(runner.Y - chaser.Y) < 24.0 * dpi)
        {
            runner.TriggerEmote("!", 0.7);
            chaser.TriggerEmote("Tag!", 0.9);
            _activeEventTimer = 0;
        }
    }

    private void StepDanceParty(IReadOnlyList<StickmanCharacter> characters, List<ParticleEffect> particles, AppSettings settings)
    {
        bool emitNotes = !settings.PerformanceMode && _subPhaseTimer >= 0.9;
        if (emitNotes)
        {
            _subPhaseTimer = 0;
        }

        foreach (int id in _groupIds)
        {
            var c = characters.FirstOrDefault(x => x.Id == id);
            if (c is null || c.IsGrabbed || !c.IsGrounded)
            {
                continue;
            }

            if (c.MoveTargetX.HasValue)
            {
                double dx = c.MoveTargetX.Value - c.X;
                if (Math.Abs(dx) <= 12.0 * c.DpiScale)
                {
                    c.Vx = 0;
                    c.MoveTargetX = null;
                    c.Facing = _gatheringX >= c.X ? 1 : -1;
                    c.State = StickmanState.Dance;
                    c.StateTime = 0;
                    c.StateDuration = 4.2;
                }
                else
                {
                    c.Facing = dx >= 0 ? 1 : -1;
                    c.Vx = c.Facing * 98.0 * c.DpiScale;
                    c.State = StickmanState.Walk;
                }
            }
            else
            {
                c.Vx = 0;
                if (c.State is not StickmanState.Dance and not StickmanState.Backflip)
                {
                    c.State = StickmanState.Dance;
                    c.StateTime = 0;
                    c.StateDuration = 3.5;
                }

                if (emitNotes && _rng.NextDouble() < 0.45)
                {
                    PhysicsEngine.SpawnSocialBurst(particles, c.X, c.Y - 56 * c.DpiScale, c.DpiScale, "♪", c.PrimaryColor);
                }

                // Grand finale flip near the end of the dance!
                if (_activeEventTimer < 0.85 && c.State == StickmanState.Dance && _rng.NextDouble() < 0.4)
                {
                    AiBehaviorController.LaunchBackflip(c);
                }
            }
        }
    }

    private void StepAcrobaticsShow(IReadOnlyList<StickmanCharacter> characters, List<ParticleEffect> particles, AppSettings settings)
    {
        var performer = characters.FirstOrDefault(c => c.Id == _primaryId);
        if (performer is null || performer.IsGrabbed)
        {
            _activeEventTimer = 0;
            return;
        }

        if (performer.IsGrounded && performer.State is StickmanState.Idle or StickmanState.Land)
        {
            if (_rng.NextDouble() < 0.5)
            {
                AiBehaviorController.LaunchBackflip(performer);
            }
            else
            {
                performer.State = StickmanState.Handstand;
                performer.StateTime = 0;
                performer.StateDuration = 2.0;
            }
        }

        if (!settings.PerformanceMode && _subPhaseTimer >= 1.1)
        {
            _subPhaseTimer = 0;
            foreach (int specId in _spectatorIds)
            {
                var spec = characters.FirstOrDefault(c => c.Id == specId);
                if (spec is not null && !spec.IsGrabbed)
                {
                    PhysicsEngine.SpawnSocialBurst(particles, spec.X, spec.Y - 54 * spec.DpiScale, spec.DpiScale, "★", spec.PrimaryColor);
                }
            }
        }
    }

    private void StepGatheringChat(IReadOnlyList<StickmanCharacter> characters, AppSettings settings)
    {
        var arrived = new List<StickmanCharacter>();
        foreach (int id in _groupIds)
        {
            var c = characters.FirstOrDefault(x => x.Id == id);
            if (c is null || c.IsGrabbed || !c.IsGrounded)
            {
                continue;
            }

            if (c.MoveTargetX.HasValue)
            {
                double dx = c.MoveTargetX.Value - c.X;
                if (Math.Abs(dx) <= 12.0 * c.DpiScale)
                {
                    c.Vx = 0;
                    c.MoveTargetX = null;
                    c.Facing = _gatheringX >= c.X ? 1 : -1;
                    c.State = StickmanState.Chat;
                    c.StateTime = 0;
                    c.StateDuration = 4.0;
                    arrived.Add(c);
                }
                else
                {
                    c.Facing = dx >= 0 ? 1 : -1;
                    c.Vx = c.Facing * 88.0 * c.DpiScale;
                    c.State = StickmanState.Walk;
                }
            }
            else
            {
                c.Vx = 0;
                c.Facing = _gatheringX >= c.X ? 1 : -1;
                if (c.State != StickmanState.Chat)
                {
                    c.State = StickmanState.Chat;
                    c.StateTime = 0;
                    c.StateDuration = 3.5;
                }
                arrived.Add(c);
            }
        }

        if (_subPhaseTimer >= 0.95 && arrived.Count > 0)
        {
            _subPhaseTimer = 0;
            var speaker = arrived[_rng.Next(arrived.Count)];
            string[] bubbles = { "...", "?", "!", "♪" };
            speaker.TriggerEmote(bubbles[_rng.Next(bubbles.Length)], 0.85);

            // Occasionally a playful argument breaks out into a duel!
            if (settings.EnableFighting && !_gatheringSparkedFight && _fightPairCooldown <= 0 && arrived.Count >= 2 && _activeEventTimer < 1.8 && _rng.NextDouble() < 0.22)
            {
                _gatheringSparkedFight = true;
                var a = arrived[0];
                var b = arrived[1];
                EndActiveEvent(characters);
                BeginSpectatedDuel(characters, a, b, settings);
            }
        }
    }

    private void EndActiveEvent(IReadOnlyList<StickmanCharacter> characters)
    {
        foreach (var c in characters)
        {
            if (c.Id == _primaryId || c.Id == _secondaryId || _groupIds.Contains(c.Id) || _spectatorIds.Contains(c.Id))
            {
                c.PartnerCharacterId = null;
                c.LookTargetCharacterId = null;
                c.MiniEventCooldown = 3.5;
                if (c.IsGrounded && c.State is not StickmanState.Sleep and not StickmanState.Fall and not StickmanState.Backflip)
                {
                    c.Vx = 0;
                    c.State = StickmanState.Idle;
                    c.StateTime = 0;
                    c.StateDuration = 0.9;
                }
            }
        }

        _activeEvent = MiniEventKind.None;
        _primaryId = null;
        _secondaryId = null;
        _groupIds.Clear();
        _spectatorIds.Clear();
    }

    private double GetGlobalCooldown(AppSettings settings) => settings.ActivityLevel switch
    {
        ActivityLevel.Calm => 10.5,
        ActivityLevel.Lively => 3.2,
        _ => 5.8
    };
}
