using System.Windows.Media;
using DesktopStickmanUniverse.Desktop;

namespace DesktopStickmanUniverse.Physics;

/// <summary>
/// Deterministic 2D physics, surface carry, swept landing, climb mantling,
/// monitor boundary handling, and character-character separation solver.
/// </summary>
public sealed class PhysicsEngine
{
    public const double GravityDipPerSecSq = 1650.0;
    private long _lastAppliedSnapshotSeq = -1;

    public event Action<StickmanCharacter, double>? CharacterLanded;
    public event Action<StickmanCharacter>? CharacterStartedFallingFromWindow;

    public void Step(
        IReadOnlyList<StickmanCharacter> characters,
        List<ParticleEffect> particles,
        DesktopEnvironmentSnapshot snapshot,
        double dt)
    {
        if (snapshot.Monitors.Count == 0)
        {
            return;
        }

        bool applySnapshotCarry = snapshot.SequenceId != _lastAppliedSnapshotSeq;
        if (applySnapshotCarry)
        {
            _lastAppliedSnapshotSeq = snapshot.SequenceId;
        }

        foreach (var c in characters)
        {
            c.DpiScale = snapshot.GetDpiScaleAt(c.X, c.Y);
            double dpi = c.DpiScale;

            if (c.IsGrabbed)
            {
                c.IsGrounded = false;
                c.GroundedSurface = null;
                c.ClimbingWall = null;
                c.FallStartY = c.Y;
                continue;
            }

            // 1. Apply one-shot window/taskbar displacement when desktop geometry updated.
            if (applySnapshotCarry)
            {
                SyncWithDesktopSnapshot(c, snapshot, dpi);
            }

            // 2. Climbing integration.
            if (c.State == StickmanState.Climb && c.ClimbingWall is not null)
            {
                StepClimbing(c, snapshot, dt, dpi);
                continue;
            }

            // 3. Grounded motion & edge-walk fall check.
            if (c.IsGrounded && c.GroundedSurface is not null)
            {
                c.X += c.Vx * dt;
                c.Y = c.GroundedSurface.Y;
                c.Vy = 0;

                // Apply ground friction when not actively propelled.
                if (c.State is not StickmanState.Walk and not StickmanState.Run)
                {
                    c.Vx *= Math.Exp(-12.0 * dt);
                    if (Math.Abs(c.Vx) < 2.0)
                    {
                        c.Vx = 0;
                    }
                }

                // Check if character walked or slid off the current surface bounds.
                double overhangAllowance = 4.0 * dpi;
                if (c.X < c.GroundedSurface.Left - overhangAllowance || c.X > c.GroundedSurface.Right + overhangAllowance)
                {
                    c.IsGrounded = false;
                    c.GroundedSurface = null;
                    c.FallStartY = c.Y;
                    c.State = StickmanState.Fall;
                    c.StateTime = 0;
                    c.StateDuration = 1.5;
                }
            }
            else
            {
                // 4. Airborne gravity + swept vertical landing collision.
                double prevY = c.Y;
                c.Vy += GravityDipPerSecSq * dpi * dt;
                c.Vy = Math.Min(c.Vy, 1450.0 * dpi);
                c.Vx *= Math.Exp(-0.8 * dt);

                double nextX = c.X + c.Vx * dt;
                double nextY = c.Y + c.Vy * dt;

                c.X = nextX;
                c.Y = nextY;

                if (c.Vy > 85.0 * dpi &&
                    c.State is not StickmanState.Fall and not StickmanState.HitReact and not StickmanState.Backflip &&
                    !(c.State == StickmanState.FightAttack && c.CombatMove == CombatMoveKind.FlyingKick))
                {
                    c.State = StickmanState.Fall;
                    c.StateTime = 0;
                }

                if (c.Vy >= 0)
                {
                    WalkableSurface? landed = FindSweptLandingSurface(snapshot.WalkableSurfaces, c.X, prevY, nextY, dpi);
                    if (landed is not null)
                    {
                        double fallHeight = c.Y - c.FallStartY;
                        c.Y = landed.Y;
                        c.Vy = 0;
                        c.IsGrounded = true;
                        c.GroundedSurface = landed;
                        c.ClimbingWall = null;

                        if (fallHeight > 240.0 * dpi)
                        {
                            c.State = StickmanState.GetUp;
                            c.StateTime = 0;
                            c.StateDuration = 0.55;
                            c.Vx *= 0.25;
                            SpawnDustBurst(particles, c.X, c.Y, dpi, 6);
                            CharacterLanded?.Invoke(c, fallHeight);
                        }
                        else if (fallHeight > 45.0 * dpi || c.State == StickmanState.Backflip)
                        {
                            c.State = StickmanState.Land;
                            c.StateTime = 0;
                            c.StateDuration = 0.16;
                            c.Vx *= 0.5;
                            SpawnDustBurst(particles, c.X, c.Y, dpi, 3);
                            CharacterLanded?.Invoke(c, fallHeight);
                        }
                        else if (c.State is StickmanState.Jump or StickmanState.Fall || (c.State == StickmanState.FightAttack && c.CombatMove == CombatMoveKind.FlyingKick))
                        {
                            c.State = Math.Abs(c.Vx) > 20 * dpi ? StickmanState.Walk : StickmanState.Idle;
                            c.StateTime = 0;
                        }
                    }
                }
            }

            // 5. Monitor boundary & multi-monitor crossing containment.
            EnforceMonitorContainment(c, snapshot, dpi);
        }

        // 6. Soft character-to-character separation to prevent stiff overlapping.
        ApplyCharacterSeparation(characters, dt);

        // 7. Update particle effects.
        StepParticles(particles, dt);
    }

    private void SyncWithDesktopSnapshot(StickmanCharacter c, DesktopEnvironmentSnapshot snapshot, double dpi)
    {
        if (c.State == StickmanState.Climb && c.ClimbingWall is not null)
        {
            var updatedWall = snapshot.ClimbableWalls.FirstOrDefault(w =>
                w.SourceHwnd == c.ClimbingWall.SourceHwnd &&
                w.Side == c.ClimbingWall.Side &&
                c.Y >= w.TopY - 12 * dpi &&
                c.Y <= w.BottomY + 12 * dpi);

            if (updatedWall is null)
            {
                c.ClimbingWall = null;
                c.State = StickmanState.Fall;
                c.StateTime = 0;
                c.FallStartY = c.Y;
                c.TriggerEmote("!", 0.8);
                CharacterStartedFallingFromWindow?.Invoke(c);
                return;
            }

            c.ClimbingWall = updatedWall;
            c.X = updatedWall.X;
            c.Y += updatedWall.DeltaY;
            return;
        }

        if (!c.IsGrounded || c.GroundedSurface is null)
        {
            return;
        }

        var prevSurf = c.GroundedSurface;
        WalkableSurface? updatedSurface = null;

        // First try matching exact surface ID (typical for taskbar/floor).
        foreach (var surf in snapshot.WalkableSurfaces)
        {
            if (surf.SurfaceId == prevSurf.SurfaceId &&
                c.X + surf.DeltaX >= surf.Left - 8 * dpi &&
                c.X + surf.DeltaX <= surf.Right + 8 * dpi)
            {
                updatedSurface = surf;
                break;
            }
        }

        // Fallback: match by SourceHwnd & SurfaceKind in case occlusion split segment indices changed.
        if (updatedSurface is null && prevSurf.SourceHwnd != IntPtr.Zero)
        {
            foreach (var surf in snapshot.WalkableSurfaces)
            {
                if (surf.SourceHwnd == prevSurf.SourceHwnd &&
                    surf.Kind == prevSurf.Kind &&
                    c.X + surf.DeltaX >= surf.Left - 10 * dpi &&
                    c.X + surf.DeltaX <= surf.Right + 10 * dpi)
                {
                    updatedSurface = surf;
                    break;
                }
            }
        }

        if (updatedSurface is null)
        {
            // Supporting window was minimized, closed, occluded, or dragged from underneath.
            c.IsGrounded = false;
            c.GroundedSurface = null;
            c.FallStartY = c.Y;
            c.State = StickmanState.Fall;
            c.StateTime = 0;
            c.StateDuration = 1.5;
            if (prevSurf.Kind == SurfaceKind.WindowTop)
            {
                c.TriggerEmote("!", 0.85);
                CharacterStartedFallingFromWindow?.Invoke(c);
            }
            return;
        }

        // If user dragged the window violently fast, detach stickman with partial momentum!
        if (updatedSurface.DeltaY > 24.0 * dpi || Math.Abs(updatedSurface.DeltaX) > 65.0 * dpi)
        {
            c.IsGrounded = false;
            c.GroundedSurface = null;
            c.FallStartY = c.Y;
            c.Vx = Math.Clamp(updatedSurface.DeltaX * 8.0, -350 * dpi, 350 * dpi);
            c.Vy = Math.Min(0, updatedSurface.DeltaY * 4.0);
            c.State = StickmanState.Fall;
            c.StateTime = 0;
            c.TriggerEmote("!", 0.9);
            CharacterStartedFallingFromWindow?.Invoke(c);
            return;
        }

        c.GroundedSurface = updatedSurface;
        c.X = Math.Clamp(c.X + updatedSurface.DeltaX, updatedSurface.Left, updatedSurface.Right);
        c.Y = updatedSurface.Y;
    }

    private static void StepClimbing(StickmanCharacter c, DesktopEnvironmentSnapshot snapshot, double dt, double dpi)
    {
        var wall = c.ClimbingWall!;
        double climbSpeed = 105.0 * dpi * c.Personality.WalkSpeedFactor;

        c.Facing = wall.Side == WallSide.Left ? 1 : -1;
        c.X = wall.X;
        c.Vx = 0;
        c.Vy = -climbSpeed;
        c.Y += c.Vy * dt;

        // Reached the top edge -> mantle smoothly onto the window's top surface!
        if (c.Y <= wall.TopY)
        {
            double stepInward = wall.Side == WallSide.Left ? 18.0 * dpi : -18.0 * dpi;
            double targetX = wall.X + stepInward;

            var topSurface = snapshot.WalkableSurfaces.FirstOrDefault(s =>
                s.SourceHwnd == wall.SourceHwnd &&
                s.Kind == SurfaceKind.WindowTop &&
                targetX >= s.Left - 8 * dpi &&
                targetX <= s.Right + 8 * dpi);

            if (topSurface is not null)
            {
                c.X = Math.Clamp(targetX, topSurface.Left + 4 * dpi, topSurface.Right - 4 * dpi);
                c.Y = topSurface.Y;
                c.Vy = 0;
                c.ClimbingWall = null;
                c.IsGrounded = true;
                c.GroundedSurface = topSurface;
                c.State = StickmanState.Idle;
                c.StateTime = 0;
                c.StateDuration = 1.0;
                c.ClimbCooldown = 4.0;
            }
            else
            {
                // Window top was off-screen or occluded: hop gently inward or drop.
                c.ClimbingWall = null;
                c.X = wall.X - stepInward * 0.5;
                c.Y = wall.TopY + 4 * dpi;
                c.Vy = 0;
                c.FallStartY = c.Y;
                c.State = StickmanState.Fall;
                c.StateTime = 0;
                c.ClimbCooldown = 3.5;
            }
        }
    }

    public static WalkableSurface? FindSweptLandingSurface(
        IReadOnlyList<WalkableSurface> surfaces,
        double x,
        double prevY,
        double nextY,
        double dpi)
    {
        WalkableSurface? best = null;
        double bestY = double.MaxValue;
        int bestZ = int.MaxValue;

        double tolerance = 6.0 * dpi;
        foreach (var s in surfaces)
        {
            if (x < s.Left - 3.0 * dpi || x > s.Right + 3.0 * dpi)
            {
                continue;
            }

            if (prevY <= s.Y + tolerance && nextY >= s.Y - 1.0)
            {
                if (s.Y < bestY - 0.5 || (Math.Abs(s.Y - bestY) <= 0.5 && s.ZOrder < bestZ))
                {
                    bestY = s.Y;
                    bestZ = s.ZOrder;
                    best = s;
                }
            }
        }

        return best;
    }

    private static void EnforceMonitorContainment(StickmanCharacter c, DesktopEnvironmentSnapshot snapshot, double dpi)
    {
        var mon = snapshot.FindMonitorAt(c.X, c.Y);
        if (mon is null)
        {
            return;
        }

        double margin = 12.0 * dpi;
        double minX = mon.WorkArea.Left + margin;
        double maxX = mon.WorkArea.Right - margin;

        // Allow seamless crossing onto an adjacent monitor if one exists at this coordinate.
        if (c.X < minX)
        {
            var leftNeighbor = snapshot.Monitors.FirstOrDefault(m =>
                m != mon && m.Bounds.Contains(c.X - margin, Math.Clamp(c.Y, m.Bounds.Top + 10, m.Bounds.Bottom - 10)));
            if (leftNeighbor is null)
            {
                c.X = minX;
                if (c.Vx < 0)
                {
                    c.Vx = -c.Vx * 0.4;
                    c.Facing = 1;
                }
            }
        }
        else if (c.X > maxX)
        {
            var rightNeighbor = snapshot.Monitors.FirstOrDefault(m =>
                m != mon && m.Bounds.Contains(c.X + margin, Math.Clamp(c.Y, m.Bounds.Top + 10, m.Bounds.Bottom - 10)));
            if (rightNeighbor is null)
            {
                c.X = maxX;
                if (c.Vx > 0)
                {
                    c.Vx = -c.Vx * 0.4;
                    c.Facing = -1;
                }
            }
        }

        double topClamp = mon.Bounds.Top + 56.0 * dpi;
        if (c.Y < topClamp)
        {
            c.Y = topClamp;
            if (c.Vy < 0)
            {
                c.Vy = 0;
            }
        }

        // Safety net floor if character somehow drops past all surfaces.
        var fallbackFloor = snapshot.WalkableSurfaces
            .Where(s => s.Kind is SurfaceKind.Taskbar or SurfaceKind.MonitorFloor)
            .FirstOrDefault(s => c.X >= s.Left - 20 * dpi && c.X <= s.Right + 20 * dpi);

        double floorY = fallbackFloor?.Y ?? mon.WorkArea.Bottom;
        if (c.Y > floorY + 8.0 * dpi)
        {
            c.Y = floorY;
            c.Vy = 0;
            c.IsGrounded = true;
            c.GroundedSurface = fallbackFloor;
            if (c.State == StickmanState.Fall)
            {
                c.State = StickmanState.Land;
                c.StateTime = 0;
                c.StateDuration = 0.16;
            }
        }
    }

    private static void ApplyCharacterSeparation(IReadOnlyList<StickmanCharacter> characters, double dt)
    {
        for (int i = 0; i < characters.Count; i++)
        {
            var a = characters[i];
            if (!a.IsGrounded || a.IsGrabbed ||
                a.State is StickmanState.FightAttack or StickmanState.FightDodge or StickmanState.Block or StickmanState.Sleep or StickmanState.Peek or StickmanState.HighFive)
            {
                continue;
            }

            for (int j = i + 1; j < characters.Count; j++)
            {
                var b = characters[j];
                if (!b.IsGrounded || b.IsGrabbed ||
                    b.State is StickmanState.FightAttack or StickmanState.FightDodge or StickmanState.Block or StickmanState.Sleep or StickmanState.Peek or StickmanState.HighFive)
                {
                    continue;
                }

                double dpi = Math.Max(a.DpiScale, b.DpiScale);
                if (Math.Abs(a.Y - b.Y) > 22.0 * dpi)
                {
                    continue;
                }

                bool sittingPair = a.State == StickmanState.SitTogether && b.State == StickmanState.SitTogether;
                double minSep = sittingPair ? 15.0 * dpi : 21.0 * dpi;
                double dx = b.X - a.X;
                double absDx = Math.Abs(dx);
                if (absDx >= minSep)
                {
                    continue;
                }

                double pushDir = absDx < 0.5 ? (a.Id < b.Id ? -1.0 : 1.0) : Math.Sign(dx);
                double overlap = (minSep - absDx) * 0.5;
                double shift = Math.Min(overlap, 80.0 * dpi * dt);

                if (a.GroundedSurface is not null)
                {
                    a.X = Math.Clamp(a.X - pushDir * shift, a.GroundedSurface.Left, a.GroundedSurface.Right);
                }
                if (b.GroundedSurface is not null)
                {
                    b.X = Math.Clamp(b.X + pushDir * shift, b.GroundedSurface.Left, b.GroundedSurface.Right);
                }
            }
        }
    }

    private static void StepParticles(List<ParticleEffect> particles, double dt)
    {
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var p = particles[i];
            p.Life += dt;
            if (p.Life >= p.MaxLife)
            {
                particles.RemoveAt(i);
                continue;
            }

            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            if (p.Kind == ParticleKind.Dot && string.IsNullOrEmpty(p.Text))
            {
                p.Vy += 220.0 * dt;
            }
        }
    }

    public static void SpawnDustBurst(List<ParticleEffect> particles, double x, double y, double dpi, int count)
    {
        for (int i = 0; i < count; i++)
        {
            double angle = -Math.PI * (0.15 + 0.7 * i / Math.Max(1, count - 1));
            double speed = (28.0 + (i % 3) * 18.0) * dpi;
            particles.Add(new ParticleEffect
            {
                Kind = ParticleKind.Dot,
                X = x + ((i % 2 == 0 ? 1 : -1) * 3.0 * dpi),
                Y = y - 2.0 * dpi,
                Vx = Math.Cos(angle) * speed,
                Vy = Math.Sin(angle) * speed * 0.65,
                Life = 0,
                MaxLife = 0.38,
                Size = 2.6 * dpi,
                Color = Color.FromArgb(165, 220, 225, 235)
            });
        }
    }

    public static void SpawnHitSpark(List<ParticleEffect> particles, double x, double y, double dpi, Color accent)
    {
        particles.Add(new ParticleEffect
        {
            Kind = ParticleKind.Star,
            X = x,
            Y = y,
            Life = 0,
            MaxLife = 0.24,
            Size = 11.0 * dpi,
            Color = Colors.White
        });

        particles.Add(new ParticleEffect
        {
            Kind = ParticleKind.Ring,
            X = x,
            Y = y,
            Life = 0,
            MaxLife = 0.28,
            Size = 4.0 * dpi,
            EndSize = 24.0 * dpi,
            Color = Color.FromArgb(220, accent.R, accent.G, accent.B)
        });

        for (int i = 0; i < 6; i++)
        {
            double angle = (Math.PI * 2.0 * i) / 6.0;
            double speed = (75.0 + (i % 2) * 35.0) * dpi;
            particles.Add(new ParticleEffect
            {
                Kind = ParticleKind.Dot,
                X = x,
                Y = y,
                Vx = Math.Cos(angle) * speed,
                Vy = Math.Sin(angle) * speed - 35.0 * dpi,
                Life = 0,
                MaxLife = 0.30,
                Size = 2.8 * dpi,
                Color = i % 2 == 0 ? Colors.White : accent
            });
        }
    }

    public static void SpawnBlockSpark(List<ParticleEffect> particles, double x, double y, double dpi)
    {
        particles.Add(new ParticleEffect
        {
            Kind = ParticleKind.Ring,
            X = x,
            Y = y,
            Life = 0,
            MaxLife = 0.22,
            Size = 4.0 * dpi,
            EndSize = 18.0 * dpi,
            Color = Color.FromArgb(215, 130, 210, 255)
        });
        particles.Add(new ParticleEffect
        {
            Kind = ParticleKind.Star,
            X = x,
            Y = y,
            Life = 0,
            MaxLife = 0.18,
            Size = 8.0 * dpi,
            Color = Color.FromRgb(180, 230, 255)
        });
    }

    public static void SpawnSocialBurst(List<ParticleEffect> particles, double x, double y, double dpi, string text, Color color)
    {
        particles.Add(new ParticleEffect
        {
            Kind = ParticleKind.Text,
            X = x,
            Y = y,
            Vx = 0,
            Vy = -28.0 * dpi,
            Life = 0,
            MaxLife = 1.0,
            Size = 12.0 * dpi,
            Color = color,
            Text = text
        });
    }
}
