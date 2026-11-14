using DesktopStickmanUniverse.Animation;
using DesktopStickmanUniverse.Physics;
using Xunit;

namespace DesktopStickmanUniverse.Tests;

public sealed class AnimationAndIkTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(12, 12)]
    [InlineData(100, 0)]
    [InlineData(-40, 20)]
    public void SolveTwoBoneIK_NeverProducesNaN_AndRespectsReachLimits(double targetX, double targetY)
    {
        var root = new Vec2(0, -24);
        double l1 = 12.6;
        double l2 = 12.6;

        var (mid, end) = SkeletonRig.SolveTwoBoneIK(root, new Vec2(targetX, targetY), l1, l2, -1);

        Assert.False(double.IsNaN(mid.X) || double.IsNaN(mid.Y));
        Assert.False(double.IsNaN(end.X) || double.IsNaN(end.Y));
        Assert.True((mid - root).Length <= l1 + 0.1);
        Assert.True((end - mid).Length <= l2 + 0.1);
    }

    [Fact]
    public void ProceduralAnimator_AllStatesProduceFiniteJointPositions()
    {
        var c = new StickmanCharacter { Id = 1, AnimPhase = 0.75, StateTime = 0.2, StateDuration = 0.5, Vy = 600 };

        foreach (StickmanState state in Enum.GetValues<StickmanState>())
        {
            c.State = state;
            var pose = ProceduralAnimator.ComputeTargetPose(c);

            Vec2[] joints =
            {
                pose.Pelvis, pose.Chest, pose.Neck, pose.HeadCenter,
                pose.ShoulderL, pose.ElbowL, pose.HandL,
                pose.ShoulderR, pose.ElbowR, pose.HandR,
                pose.HipL, pose.KneeL, pose.FootL,
                pose.HipR, pose.KneeR, pose.FootR
            };

            foreach (var j in joints)
            {
                Assert.False(double.IsNaN(j.X) || double.IsInfinity(j.X), $"Invalid X for state {state}");
                Assert.False(double.IsNaN(j.Y) || double.IsInfinity(j.Y), $"Invalid Y for state {state}");
            }
        }
    }

    [Fact]
    public void ProceduralAnimator_AllCombatMovesProduceFiniteJointPositions()
    {
        var c = new StickmanCharacter
        {
            Id = 1,
            State = StickmanState.FightAttack,
            StateDuration = 0.45,
            AnimPhase = 1.2
        };

        double[] sampleTimes = { 0.05, 0.20, 0.40 };
        foreach (CombatMoveKind move in Enum.GetValues<CombatMoveKind>())
        {
            c.CombatMove = move;
            foreach (double t in sampleTimes)
            {
                c.StateTime = t;
                var pose = ProceduralAnimator.ComputeTargetPose(c);

                Vec2[] joints =
                {
                    pose.Pelvis, pose.Chest, pose.Neck, pose.HeadCenter,
                    pose.ShoulderL, pose.ElbowL, pose.HandL,
                    pose.ShoulderR, pose.ElbowR, pose.HandR,
                    pose.HipL, pose.KneeL, pose.FootL,
                    pose.HipR, pose.KneeR, pose.FootR
                };

                foreach (var j in joints)
                {
                    Assert.False(double.IsNaN(j.X) || double.IsInfinity(j.X), $"Invalid X for move {move} at t={t}");
                    Assert.False(double.IsNaN(j.Y) || double.IsInfinity(j.Y), $"Invalid Y for move {move} at t={t}");
                }
            }
        }
    }
}
