using DesktopStickmanUniverse.Physics;

namespace DesktopStickmanUniverse.Animation;

/// <summary>
/// Generates articulated procedural poses from character physics and behavioral state.
/// </summary>
public static class ProceduralAnimator
{
    public static StickmanPose ComputeTargetPose(StickmanCharacter character)
    {
        double s = character.VisualScale;
        int facing = character.Facing >= 0 ? 1 : -1;
        double t = character.AnimPhase;

        double thighLen = 12.6 * s;
        double shinLen = 12.6 * s;
        double upperArmLen = 10.0 * s;
        double foreArmLen = 9.5 * s;

        var pose = new StickmanPose
        {
            HeadRadius = 6.8 * s,
            Facing = facing,
            EyeLookOffset = character.EyeLookOffset
        };

        // Knee bends forward in screen coordinates (positive X when facing right, negative Y is up).
        // Because Y points downward, for a downward leg vector (0, +1), perp=(-1, 0):
        // bendSign = -facing yields knee offset toward +facing!
        double kneeBend = -facing;
        double elbowBend = facing;

        switch (character.State)
        {
            case StickmanState.Walk:
            case StickmanState.Run:
            {
                bool isRun = character.State == StickmanState.Run;
                double strideFreq = isRun ? 11.0 : 6.6;
                double phase = t * strideFreq;
                double strideLen = (isRun ? 13.5 : 8.5) * s;
                double footLift = (isRun ? 8.2 : 4.2) * s;
                double lean = (isRun ? 6.5 : 2.5) * facing * s;
                double bob = Math.Abs(Math.Sin(phase)) * (isRun ? 3.2 : 1.6) * s;

                pose.Pelvis = new Vec2(lean * 0.25, -23.5 * s - bob);
                pose.Chest = new Vec2(lean * 0.65, -34.0 * s - bob);
                pose.Neck = new Vec2(lean * 0.9, -40.0 * s - bob);
                pose.HeadCenter = new Vec2(lean * 1.05, -46.8 * s - bob);

                double swingL = Math.Sin(phase);
                double liftL = Math.Max(0, Math.Cos(phase));
                double swingR = Math.Sin(phase + Math.PI);
                double liftR = Math.Max(0, Math.Cos(phase + Math.PI));

                Vec2 footTargetL = new(swingL * strideLen * facing, -liftL * footLift);
                Vec2 footTargetR = new(swingR * strideLen * facing, -liftR * footLift);

                pose.HipL = pose.Pelvis + new Vec2(-1.5 * s * facing, 0);
                pose.HipR = pose.Pelvis + new Vec2(1.5 * s * facing, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, footTargetL, thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, footTargetR, thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-1.8 * s * facing, 2.2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(1.8 * s * facing, 2.2 * s);

                double armReach = (isRun ? 11.5 : 7.5) * s;
                double armLift = isRun ? 5.0 * s : 2.0 * s;
                Vec2 handTargetL = pose.ShoulderL + new Vec2(swingR * armReach * facing, 14.5 * s - Math.Abs(swingR) * armLift);
                Vec2 handTargetR = pose.ShoulderR + new Vec2(swingL * armReach * facing, 14.5 * s - Math.Abs(swingL) * armLift);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, handTargetL, upperArmLen, foreArmLen, elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, handTargetR, upperArmLen, foreArmLen, elbowBend);
                pose.EyeExpression = isRun ? EyeExpression.Determined : EyeExpression.Normal;
                break;
            }

            case StickmanState.Jump:
            {
                double rise = Math.Clamp(-character.Vy / (500.0 * Math.Max(1.0, character.DpiScale)), 0.0, 1.0);
                double tuck = 4.0 * rise * s;
                pose.Pelvis = new Vec2(1.5 * facing * s, -24.5 * s);
                pose.Chest = new Vec2(2.5 * facing * s, -35.0 * s);
                pose.Neck = new Vec2(3.0 * facing * s, -41.0 * s);
                pose.HeadCenter = new Vec2(3.2 * facing * s, -48.0 * s);

                pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-5 * s * facing, -3 * s - tuck), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(5 * s * facing, -5 * s - tuck), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.ShoulderL + new Vec2(-8 * s * facing, -11 * s), upperArmLen, foreArmLen, -elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.ShoulderR + new Vec2(9 * s * facing, -12 * s), upperArmLen, foreArmLen, elbowBend);
                pose.EyeExpression = EyeExpression.Determined;
                break;
            }

            case StickmanState.Fall:
            {
                double flail = Math.Sin(t * 14.0) * 4.5 * s;
                bool fastFall = character.Vy > 520.0 * Math.Max(1.0, character.DpiScale);
                pose.Pelvis = new Vec2(0, -24.0 * s);
                pose.Chest = new Vec2(-1.5 * facing * s, -34.0 * s);
                pose.Neck = new Vec2(-2.0 * facing * s, -40.0 * s);
                pose.HeadCenter = new Vec2(-2.2 * facing * s, -46.8 * s);

                pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-6 * s + flail * 0.4, -4 * s), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(6 * s - flail * 0.4, -2 * s), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.ShoulderL + new Vec2(-11 * s, -10 * s + flail), upperArmLen, foreArmLen, -elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.ShoulderR + new Vec2(11 * s, -10 * s - flail), upperArmLen, foreArmLen, elbowBend);
                pose.EyeExpression = fastFall ? EyeExpression.Surprised : EyeExpression.Normal;
                break;
            }

            case StickmanState.Land:
            case StickmanState.GetUp:
            {
                double squash = character.State == StickmanState.Land ? 7.5 * s : 5.5 * s;
                pose.Pelvis = new Vec2(-1.5 * facing * s, -22.5 * s + squash);
                pose.Chest = new Vec2(2.5 * facing * s, -32.0 * s + squash);
                pose.Neck = new Vec2(3.5 * facing * s, -37.5 * s + squash);
                pose.HeadCenter = new Vec2(4.0 * facing * s, -43.5 * s + squash);

                pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-6.5 * s, 0), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(6.5 * s, 0), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, new Vec2(-9 * s, -4 * s), upperArmLen, foreArmLen, elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, new Vec2(9 * s, -4 * s), upperArmLen, foreArmLen, elbowBend);
                pose.EyeExpression = character.State == StickmanState.GetUp ? EyeExpression.Dizzy : EyeExpression.Normal;
                break;
            }

            case StickmanState.Sit:
            case StickmanState.SitTogether:
            {
                // Character sits directly on top edge (y = 0) with knees bent and lower legs dangling below y = 0.
                double dangle = Math.Sin(t * 2.8) * 3.8 * s;
                double dangle2 = Math.Sin(t * 2.8 + 1.4) * 3.8 * s;
                double breath = Math.Sin(t * 2.1) * 0.8 * s;
                double headNod = character.State == StickmanState.SitTogether ? Math.Sin(t * 4.0) * 1.2 * s : 0;

                pose.Pelvis = new Vec2(-2.0 * facing * s, -3.0 * s);
                pose.Chest = new Vec2(-1.5 * facing * s, -13.5 * s + breath);
                pose.Neck = new Vec2(-0.5 * facing * s, -19.5 * s + breath);
                pose.HeadCenter = new Vec2(0.5 * facing * s + headNod * 0.4, -26.5 * s + breath + headNod * 0.5);

                pose.HipL = pose.Pelvis + new Vec2(-1.5 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(1.5 * s, 0);
                pose.KneeL = new Vec2(6.5 * facing * s, -1.5 * s);
                pose.KneeR = new Vec2(8.0 * facing * s, -1.0 * s);
                pose.FootL = new Vec2(7.0 * facing * s + dangle, 10.5 * s);
                pose.FootR = new Vec2(8.5 * facing * s + dangle2, 10.5 * s);

                pose.ShoulderL = pose.Neck + new Vec2(-1.5 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(1.5 * s, 2 * s);
                pose.ElbowL = pose.Pelvis + new Vec2(-5.5 * facing * s, -5 * s);
                pose.HandL = new Vec2(-5.5 * facing * s, 0);
                pose.ElbowR = pose.Pelvis + new Vec2(4.0 * facing * s, -5 * s);
                pose.HandR = new Vec2(4.5 * facing * s, 0);
                pose.EyeExpression = EyeExpression.Normal;
                break;
            }

            case StickmanState.Sleep:
            {
                double snore = Math.Sin(t * 1.8) * 1.1 * s;
                pose.Pelvis = new Vec2(-6.0 * facing * s, -4.5 * s);
                pose.Chest = new Vec2(2.0 * facing * s, -5.5 * s + snore);
                pose.Neck = new Vec2(7.5 * facing * s, -6.0 * s + snore);
                pose.HeadCenter = new Vec2(13.5 * facing * s, -6.8 * s + snore * 0.6);

                pose.HipL = pose.Pelvis;
                pose.HipR = pose.Pelvis;
                pose.KneeL = new Vec2(-12.0 * facing * s, -8.5 * s);
                pose.FootL = new Vec2(-17.0 * facing * s, -1.0 * s);
                pose.KneeR = new Vec2(-10.0 * facing * s, -7.0 * s);
                pose.FootR = new Vec2(-14.5 * facing * s, 0);

                pose.ShoulderL = pose.Neck;
                pose.ShoulderR = pose.Neck;
                pose.ElbowL = new Vec2(11.0 * facing * s, -1.5 * s);
                pose.HandL = new Vec2(15.0 * facing * s, -1.0 * s);
                pose.ElbowR = new Vec2(9.0 * facing * s, -2.0 * s);
                pose.HandR = new Vec2(12.5 * facing * s, -0.5 * s);
                pose.EyeExpression = EyeExpression.Sleep;
                break;
            }

            case StickmanState.Climb:
            {
                double climbCycle = t * 7.2;
                double armCycleL = Math.Sin(climbCycle);
                double armCycleR = Math.Sin(climbCycle + Math.PI);
                double wallSide = facing; // facing toward the wall edge (+1 climbing right-side, -1 climbing left-side)
                double bodyOffset = -8.5 * wallSide * s;

                pose.Pelvis = new Vec2(bodyOffset, -23.0 * s);
                pose.Chest = new Vec2(bodyOffset + 1.5 * wallSide * s, -33.5 * s);
                pose.Neck = new Vec2(bodyOffset + 2.5 * wallSide * s, -39.5 * s);
                pose.HeadCenter = new Vec2(bodyOffset + 2.5 * wallSide * s, -46.2 * s);

                pose.HipL = pose.Pelvis + new Vec2(-1.5 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(1.5 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(
                    pose.HipL,
                    new Vec2(0, -10.5 * s + armCycleR * 6.0 * s),
                    thighLen,
                    shinLen,
                    -wallSide);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(
                    pose.HipR,
                    new Vec2(0, -10.5 * s + armCycleL * 6.0 * s),
                    thighLen,
                    shinLen,
                    -wallSide);

                pose.ShoulderL = pose.Neck + new Vec2(-1 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(1 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(
                    pose.ShoulderL,
                    new Vec2(0, -43.0 * s + armCycleL * 7.0 * s),
                    upperArmLen,
                    foreArmLen,
                    wallSide);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(
                    pose.ShoulderR,
                    new Vec2(0, -43.0 * s + armCycleR * 7.0 * s),
                    upperArmLen,
                    foreArmLen,
                    wallSide);
                pose.EyeExpression = EyeExpression.Determined;
                break;
            }

            case StickmanState.Grabbed:
            {
                // Held near head/neck; legs dangle with inertia lag.
                double swing = Math.Clamp(-character.Vx * 0.025, -18.0, 18.0) * s;
                double kick = Math.Sin(t * 9.5) * 4.5 * s;

                pose.HeadCenter = new Vec2(0, -45.0 * s);
                pose.Neck = new Vec2(swing * 0.25, -38.5 * s);
                pose.Chest = new Vec2(swing * 0.45, -32.5 * s);
                pose.Pelvis = new Vec2(swing * 0.75, -22.5 * s);

                pose.HipL = pose.Pelvis + new Vec2(-1.8 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(1.8 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(swing * 1.15 - 4 * s + kick, -1.5 * s), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(swing * 1.15 + 4 * s - kick, -0.5 * s), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.ShoulderL + new Vec2(-9 * s + swing * 0.4, -7 * s), upperArmLen, foreArmLen, -elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.ShoulderR + new Vec2(9 * s + swing * 0.4, -7 * s), upperArmLen, foreArmLen, elbowBend);
                pose.EyeExpression = EyeExpression.Surprised;
                break;
            }

            case StickmanState.FightAttack:
            {
                double p = Math.Clamp(character.StateTime / Math.Max(0.15, character.StateDuration), 0.0, 1.0);
                double strike = Math.Sin(p * Math.PI);

                switch (character.CombatMove)
                {
                    case CombatMoveKind.ComboPunch:
                    {
                        double flurryL = Math.Max(0, Math.Sin(p * Math.PI * 3.0));
                        double flurryR = Math.Max(0, Math.Sin((p * Math.PI * 3.0) + Math.PI * 0.8));
                        double lunge = 6.5 * facing * s;

                        pose.Pelvis = new Vec2(lunge * 0.5, -23.0 * s);
                        pose.Chest = new Vec2(lunge * 0.9, -33.0 * s);
                        pose.Neck = new Vec2(lunge * 1.1, -39.0 * s);
                        pose.HeadCenter = new Vec2(lunge * 1.15, -45.5 * s);

                        pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                        pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                        (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-7.5 * facing * s, 0), thighLen, shinLen, kneeBend);
                        (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(7.5 * facing * s, 0), thighLen, shinLen, kneeBend);

                        pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                        pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                        (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(
                            pose.ShoulderL,
                            pose.ShoulderL + new Vec2((3 + 15 * flurryL) * facing * s, -2 * s),
                            upperArmLen,
                            foreArmLen,
                            elbowBend);
                        (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(
                            pose.ShoulderR,
                            pose.ShoulderR + new Vec2((3 + 16 * flurryR) * facing * s, -1 * s),
                            upperArmLen,
                            foreArmLen,
                            elbowBend);
                        break;
                    }

                    case CombatMoveKind.Uppercut:
                    {
                        double rise = Math.Sin(p * Math.PI);
                        double crouch = p < 0.35 ? (1.0 - p / 0.35) * 6.0 * s : 0;
                        double lunge = rise * 7.0 * facing * s;

                        pose.Pelvis = new Vec2(lunge * 0.4, -23.5 * s + crouch - rise * 3.0 * s);
                        pose.Chest = new Vec2(lunge * 0.7, -33.8 * s + crouch - rise * 3.5 * s);
                        pose.Neck = new Vec2(lunge * 0.85, -39.8 * s + crouch - rise * 3.5 * s);
                        pose.HeadCenter = new Vec2(lunge * 0.9, -46.5 * s + crouch - rise * 3.5 * s);

                        pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                        pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                        (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-6 * facing * s, 0), thighLen, shinLen, kneeBend);
                        (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(7 * facing * s, 0), thighLen, shinLen, kneeBend);

                        pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                        pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                        (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.ShoulderL + new Vec2(-5 * facing * s, 6 * s), upperArmLen, foreArmLen, -elbowBend);
                        (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(
                            pose.ShoulderR,
                            pose.ShoulderR + new Vec2((6 + 7 * rise) * facing * s, 6 * s - 21 * rise * s),
                            upperArmLen,
                            foreArmLen,
                            elbowBend);
                        break;
                    }

                    case CombatMoveKind.FlyingKick:
                    {
                        pose.Pelvis = new Vec2(0, -25.0 * s);
                        pose.Chest = new Vec2(-2.0 * facing * s, -35.0 * s);
                        pose.Neck = new Vec2(-3.0 * facing * s, -41.0 * s);
                        pose.HeadCenter = new Vec2(-3.2 * facing * s, -47.5 * s);

                        pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                        pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                        (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-7 * facing * s, -5 * s), thighLen, shinLen, kneeBend);
                        (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(18 * facing * s, -9 * s), thighLen, shinLen, kneeBend);

                        pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                        pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                        (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.ShoulderL + new Vec2(-11 * facing * s, -4 * s), upperArmLen, foreArmLen, -elbowBend);
                        (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.ShoulderR + new Vec2(10 * facing * s, -2 * s), upperArmLen, foreArmLen, elbowBend);
                        pose.RotateAround(pose.Pelvis, -0.36 * facing);
                        break;
                    }

                    case CombatMoveKind.Kick:
                    {
                        double lunge = strike * 6.0 * facing * s;
                        pose.Pelvis = new Vec2(lunge * 0.4, -23.0 * s);
                        pose.Chest = new Vec2(lunge * 0.6, -33.2 * s);
                        pose.Neck = new Vec2(lunge * 0.7, -39.2 * s);
                        pose.HeadCenter = new Vec2(lunge * 0.75, -45.8 * s);

                        pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                        pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                        (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-5 * facing * s, 0), thighLen, shinLen, kneeBend);
                        (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(17 * strike * facing * s, -17 * strike * s), thighLen, shinLen, kneeBend);

                        pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                        pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                        (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.ShoulderL + new Vec2(-7 * facing * s, 4 * s), upperArmLen, foreArmLen, -elbowBend);
                        (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.ShoulderR + new Vec2(6 * facing * s, 3 * s), upperArmLen, foreArmLen, elbowBend);
                        break;
                    }

                    case CombatMoveKind.Punch:
                    default:
                    {
                        double lunge = strike * 7.5 * facing * s;
                        pose.Pelvis = new Vec2(lunge * 0.5, -23.0 * s);
                        pose.Chest = new Vec2(lunge * 0.9, -33.0 * s);
                        pose.Neck = new Vec2(lunge * 1.05, -39.0 * s);
                        pose.HeadCenter = new Vec2(lunge * 1.1, -45.5 * s);

                        pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                        pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                        (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-7 * facing * s, 0), thighLen, shinLen, kneeBend);
                        (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(7 * facing * s, 0), thighLen, shinLen, kneeBend);

                        pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                        pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                        (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.ShoulderL + new Vec2(-5 * facing * s, 7 * s), upperArmLen, foreArmLen, elbowBend);
                        (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.ShoulderR + new Vec2((4 + 16 * strike) * facing * s, -2 * s), upperArmLen, foreArmLen, elbowBend);
                        break;
                    }
                }

                pose.EyeExpression = EyeExpression.Determined;
                break;
            }

            case StickmanState.Block:
            {
                pose.Pelvis = new Vec2(-2.0 * facing * s, -22.0 * s);
                pose.Chest = new Vec2(-1.0 * facing * s, -32.0 * s);
                pose.Neck = new Vec2(-0.5 * facing * s, -38.0 * s);
                pose.HeadCenter = new Vec2(0, -44.2 * s);

                pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-7.5 * facing * s, 0), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(6.5 * facing * s, 0), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.HeadCenter + new Vec2(7 * facing * s, -3 * s), upperArmLen, foreArmLen, elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.HeadCenter + new Vec2(6 * facing * s, 2 * s), upperArmLen, foreArmLen, elbowBend);
                pose.EyeExpression = EyeExpression.Determined;
                break;
            }

            case StickmanState.FightDodge:
            {
                double duck = Math.Sin(Math.Clamp(character.StateTime / Math.Max(0.15, character.StateDuration), 0.0, 1.0) * Math.PI);
                double drop = 9.0 * duck * s;
                pose.Pelvis = new Vec2(-4.0 * facing * s, -23.0 * s + drop);
                pose.Chest = new Vec2(-6.0 * facing * s, -32.5 * s + drop);
                pose.Neck = new Vec2(-6.5 * facing * s, -38.0 * s + drop);
                pose.HeadCenter = new Vec2(-6.0 * facing * s, -44.2 * s + drop);

                pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-8 * facing * s, 0), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(5 * facing * s, 0), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.HeadCenter + new Vec2(5 * facing * s, 2 * s), upperArmLen, foreArmLen, elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.HeadCenter + new Vec2(7 * facing * s, 4 * s), upperArmLen, foreArmLen, elbowBend);
                pose.EyeExpression = EyeExpression.Surprised;
                break;
            }

            case StickmanState.HitReact:
            {
                double recoil = Math.Sin(Math.Clamp(character.StateTime / 0.35, 0.0, 1.0) * Math.PI);
                double leanBack = -7.0 * facing * recoil * s;
                pose.Pelvis = new Vec2(leanBack * 0.4, -23.5 * s);
                pose.Chest = new Vec2(leanBack * 0.9, -33.5 * s);
                pose.Neck = new Vec2(leanBack * 1.15, -39.5 * s);
                pose.HeadCenter = new Vec2(leanBack * 1.35, -46.0 * s);

                pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-6 * s, 0), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(6 * s, 0), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.ShoulderL + new Vec2(-9 * s, -4 * s), upperArmLen, foreArmLen, -elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.ShoulderR + new Vec2(9 * s, -6 * s), upperArmLen, foreArmLen, elbowBend);
                pose.EyeExpression = EyeExpression.Dizzy;
                break;
            }

            case StickmanState.Wave:
            {
                double waveOsc = Math.Sin(t * 10.5) * 5.5 * s;
                double bounce = Math.Abs(Math.Sin(t * 5.25)) * 1.8 * s;

                pose.Pelvis = new Vec2(0, -24.0 * s - bounce);
                pose.Chest = new Vec2(0, -34.2 * s - bounce);
                pose.Neck = new Vec2(0, -40.2 * s - bounce);
                pose.HeadCenter = new Vec2(1.0 * facing * s, -47.0 * s - bounce);

                pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-4.5 * s, 0), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(4.5 * s, 0), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.ShoulderL + new Vec2(-5 * facing * s, 13 * s), upperArmLen, foreArmLen, -elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.HeadCenter + new Vec2(9 * facing * s + waveOsc, -7 * s), upperArmLen, foreArmLen, elbowBend);
                pose.EyeExpression = EyeExpression.Happy;
                break;
            }

            case StickmanState.Brace:
            {
                double sway = Math.Sin(character.StateTime * 13) * Math.Max(0, 1 - character.StateTime) * 3.0 * s;
                pose.Pelvis = new Vec2(-1.5 * facing * s, -18.5 * s);
                pose.Chest = new Vec2(sway, -28 * s);
                pose.Neck = new Vec2(sway + 1.5 * facing * s, -34 * s);
                pose.HeadCenter = pose.Neck + new Vec2(1.5 * facing * s, -6.5 * s);
                pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-8 * s, 0), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(8 * s, 0), thighLen, shinLen, kneeBend);
                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.ShoulderL + new Vec2(-14 * s, -sway), upperArmLen, foreArmLen, -elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.ShoulderR + new Vec2(14 * s, sway), upperArmLen, foreArmLen, elbowBend);
                break;
            }

            case StickmanState.Peek:
            {
                double tilt = Math.Sin(t * 3.0) * 1.5 * s;
                pose.Pelvis = new Vec2(-2.5 * facing * s, -22.5 * s);
                pose.Chest = new Vec2(1.5 * facing * s, -32.0 * s);
                pose.Neck = new Vec2(4.0 * facing * s, -37.5 * s);
                pose.HeadCenter = new Vec2(6.0 * facing * s + tilt, -44.0 * s);

                pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-5 * s, 0), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(4 * s, 0), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.ShoulderL + new Vec2(-4 * s, 10 * s), upperArmLen, foreArmLen, elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.HeadCenter + new Vec2(4 * facing * s, 5 * s), upperArmLen, foreArmLen, elbowBend);
                pose.EyeExpression = EyeExpression.Normal;
                break;
            }

            case StickmanState.HighFive:
            {
                double p = Math.Clamp(character.StateTime / Math.Max(0.2, character.StateDuration), 0.0, 1.0);
                double clap = Math.Sin(p * Math.PI);
                double hop = clap * 4.5 * s;

                pose.Pelvis = new Vec2(1.5 * facing * s, -24.0 * s - hop);
                pose.Chest = new Vec2(2.5 * facing * s, -34.2 * s - hop);
                pose.Neck = new Vec2(3.0 * facing * s, -40.2 * s - hop);
                pose.HeadCenter = new Vec2(3.5 * facing * s, -47.0 * s - hop);

                pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-4 * s, -hop * 0.4), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(5 * facing * s, -hop * 0.5), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.ShoulderL + new Vec2(-6 * facing * s, 11 * s), upperArmLen, foreArmLen, -elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.HeadCenter + new Vec2(9 * facing * s, -7 * clap * s), upperArmLen, foreArmLen, elbowBend);
                pose.EyeExpression = EyeExpression.Happy;
                break;
            }

            case StickmanState.Chat:
            {
                double nod = Math.Sin(t * 5.2) * 1.6 * s;
                double gestureA = Math.Sin(t * 7.5) * 4.5 * s;
                double gestureB = Math.Cos(t * 6.2) * 4.0 * s;

                pose.Pelvis = new Vec2(0, -24.0 * s);
                pose.Chest = new Vec2(1.2 * facing * s, -34.2 * s);
                pose.Neck = new Vec2(1.8 * facing * s, -40.2 * s);
                pose.HeadCenter = new Vec2(2.2 * facing * s, -47.0 * s + nod);

                pose.HipL = pose.Pelvis + new Vec2(-1.8 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(1.8 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-4.5 * s, 0), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(4.8 * s, 0), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.Chest + new Vec2(6 * facing * s + gestureB * 0.4, 3 * s - Math.Abs(gestureB)), upperArmLen, foreArmLen, elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.Chest + new Vec2(8 * facing * s, -2 * s + gestureA), upperArmLen, foreArmLen, elbowBend);
                pose.EyeExpression = ((int)(t * 1.2) % 4 == 0) ? EyeExpression.Happy : EyeExpression.Normal;
                break;
            }

            case StickmanState.Cheer:
            {
                double jumpOsc = Math.Abs(Math.Sin(t * 7.2)) * 6.0 * s;
                double armPump = Math.Sin(t * 14.4) * 3.5 * s;

                pose.Pelvis = new Vec2(0, -24.0 * s - jumpOsc);
                pose.Chest = new Vec2(0, -34.2 * s - jumpOsc);
                pose.Neck = new Vec2(0, -40.2 * s - jumpOsc);
                pose.HeadCenter = new Vec2(0.8 * facing * s, -47.0 * s - jumpOsc);

                pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-5 * s, -jumpOsc * 0.35), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(5 * s, -jumpOsc * 0.35), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.HeadCenter + new Vec2(-8 * s, -10 * s + armPump), upperArmLen, foreArmLen, -elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.HeadCenter + new Vec2(8 * s, -10 * s - armPump), upperArmLen, foreArmLen, elbowBend);
                pose.EyeExpression = EyeExpression.Happy;
                break;
            }

            case StickmanState.Stretch:
            {
                double reach = (0.5 + 0.5 * Math.Sin(t * 2.6)) * 5.0 * s;
                pose.Pelvis = new Vec2(0, -24.2 * s);
                pose.Chest = new Vec2(-1.5 * facing * s, -34.6 * s - reach * 0.25);
                pose.Neck = new Vec2(-2.0 * facing * s, -40.8 * s - reach * 0.35);
                pose.HeadCenter = new Vec2(-2.2 * facing * s, -47.5 * s - reach * 0.35);

                pose.HipL = pose.Pelvis + new Vec2(-1.8 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(1.8 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-4 * s, 0), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(4 * s, 0), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.HeadCenter + new Vec2(-4 * s, -11 * s - reach), upperArmLen, foreArmLen, -elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.HeadCenter + new Vec2(4 * s, -11 * s - reach), upperArmLen, foreArmLen, elbowBend);
                pose.EyeExpression = EyeExpression.Happy;
                break;
            }

            case StickmanState.Think:
            {
                double headTilt = Math.Sin(t * 1.8) * 1.2 * s;
                pose.Pelvis = new Vec2(-0.8 * facing * s, -24.0 * s);
                pose.Chest = new Vec2(-1.0 * facing * s, -34.2 * s);
                pose.Neck = new Vec2(-0.5 * facing * s, -40.2 * s);
                pose.HeadCenter = new Vec2(headTilt, -47.0 * s);

                pose.HipL = pose.Pelvis + new Vec2(-1.8 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(1.8 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-4.5 * s, 0), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(4.5 * s, 0), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.Pelvis + new Vec2(-4 * facing * s, -1 * s), upperArmLen, foreArmLen, -elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.HeadCenter + new Vec2(2.5 * facing * s, 5 * s), upperArmLen, foreArmLen, elbowBend);
                pose.EyeLookOffset = new Vec2(0.8 * facing, -1.0);
                pose.EyeExpression = EyeExpression.Normal;
                break;
            }

            case StickmanState.Dance:
            {
                double beat = t * 7.6;
                double grooveX = Math.Sin(beat * 0.5) * 4.5 * s;
                double bounceY = Math.Abs(Math.Sin(beat)) * 3.8 * s;
                double armL = Math.Sin(beat) * 7.0 * s;
                double armR = Math.Sin(beat + Math.PI) * 7.0 * s;

                pose.Pelvis = new Vec2(grooveX * 0.6, -23.5 * s + bounceY);
                pose.Chest = new Vec2(grooveX, -33.5 * s + bounceY);
                pose.Neck = new Vec2(grooveX * 1.1, -39.5 * s + bounceY);
                pose.HeadCenter = new Vec2(grooveX * 1.15 + Math.Cos(beat * 0.5) * 1.5 * s, -46.2 * s + bounceY);

                pose.HipL = pose.Pelvis + new Vec2(-2 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(2 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-6.5 * s, -Math.Max(0, Math.Sin(beat)) * 3.0 * s), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(6.5 * s, -Math.Max(0, -Math.Sin(beat)) * 3.0 * s), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.ShoulderL + new Vec2(-9 * s, -4 * s + armL), upperArmLen, foreArmLen, -elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.ShoulderR + new Vec2(9 * s, -4 * s + armR), upperArmLen, foreArmLen, elbowBend);
                pose.EyeExpression = EyeExpression.Happy;
                break;
            }

            case StickmanState.Backflip:
            {
                double p = Math.Clamp(character.StateTime / Math.Max(0.2, character.StateDuration), 0.0, 1.0);
                // Tucked acrobatic body rotated smoothly over 360 degrees in flight.
                pose.Pelvis = new Vec2(0, -23.0 * s);
                pose.Chest = new Vec2(0, -32.5 * s);
                pose.Neck = new Vec2(0, -38.0 * s);
                pose.HeadCenter = new Vec2(0, -44.2 * s);

                pose.HipL = pose.Pelvis + new Vec2(-1.8 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(1.8 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(4 * facing * s, -12 * s), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(5 * facing * s, -11 * s), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2 * s, 2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2 * s, 2 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, pose.KneeL, upperArmLen, foreArmLen, elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, pose.KneeR, upperArmLen, foreArmLen, elbowBend);

                double angle = -facing * p * Math.PI * 2.0;
                pose.RotateAround(new Vec2(0, -25.0 * s), angle);
                pose.EyeExpression = EyeExpression.Determined;
                break;
            }

            case StickmanState.Handstand:
            {
                double sway = Math.Sin(t * 3.0) * 3.2 * s;
                double scissor = Math.Sin(t * 2.2) * 4.5 * s;

                pose.HeadCenter = new Vec2(sway * 0.2, -13.0 * s);
                pose.Neck = new Vec2(sway * 0.3, -18.8 * s);
                pose.Chest = new Vec2(sway * 0.45, -24.5 * s);
                pose.Pelvis = new Vec2(sway * 0.7, -34.5 * s);

                pose.ShoulderL = pose.Neck + new Vec2(-2.2 * s, 0);
                pose.ShoulderR = pose.Neck + new Vec2(2.2 * s, 0);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, new Vec2(-5.5 * s, 0), upperArmLen, foreArmLen, -elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, new Vec2(5.5 * s, 0), upperArmLen, foreArmLen, elbowBend);

                pose.HipL = pose.Pelvis + new Vec2(-1.8 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(1.8 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(sway - 3.5 * s + scissor, -56.0 * s), thighLen, shinLen, -kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(sway + 3.5 * s - scissor, -55.0 * s), thighLen, shinLen, kneeBend);
                pose.EyeExpression = EyeExpression.Determined;
                break;
            }

            case StickmanState.Pushups:
            {
                double rep = 0.5 + 0.5 * Math.Sin(t * 5.2);
                double chestH = (-12.5 + rep * 7.0) * s;
                double pelvisH = (-7.5 + rep * 3.8) * s;

                pose.FootL = new Vec2(-16.0 * facing * s, 0);
                pose.FootR = new Vec2(-14.5 * facing * s, 0);
                pose.KneeL = new Vec2(-9.5 * facing * s, pelvisH * 0.52);
                pose.KneeR = new Vec2(-8.5 * facing * s, pelvisH * 0.52);

                pose.Pelvis = new Vec2(-3.0 * facing * s, pelvisH);
                pose.HipL = pose.Pelvis;
                pose.HipR = pose.Pelvis;

                pose.Chest = new Vec2(6.5 * facing * s, chestH);
                pose.Neck = new Vec2(11.5 * facing * s, chestH - 1.0 * s);
                pose.HeadCenter = new Vec2(16.8 * facing * s, chestH - 1.5 * s);

                pose.ShoulderL = pose.Neck + new Vec2(-1 * s, 1 * s);
                pose.ShoulderR = pose.Neck + new Vec2(1 * s, 1 * s);
                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, new Vec2(11.5 * facing * s, 0), upperArmLen, foreArmLen, -facing);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, new Vec2(12.5 * facing * s, 0), upperArmLen, foreArmLen, -facing);
                pose.EyeExpression = EyeExpression.Determined;
                break;
            }

            case StickmanState.Idle:
            case StickmanState.LookAround:
            default:
            {
                double breathe = Math.Sin(t * 2.4) * 1.1 * s;
                double sway = Math.Sin(t * 1.1) * 1.0 * s;
                double headTilt = character.State == StickmanState.LookAround ? Math.Sin(t * 3.2) * 2.2 * s : 0;

                pose.Pelvis = new Vec2(sway * 0.5, -24.0 * s + breathe * 0.4);
                pose.Chest = new Vec2(sway * 0.8, -34.2 * s + breathe);
                pose.Neck = new Vec2(sway, -40.2 * s + breathe);
                pose.HeadCenter = new Vec2(sway + headTilt, -47.0 * s + breathe * 0.8);

                pose.HipL = pose.Pelvis + new Vec2(-1.8 * s, 0);
                pose.HipR = pose.Pelvis + new Vec2(1.8 * s, 0);
                (pose.KneeL, pose.FootL) = SkeletonRig.SolveTwoBoneIK(pose.HipL, new Vec2(-4.2 * s, 0), thighLen, shinLen, kneeBend);
                (pose.KneeR, pose.FootR) = SkeletonRig.SolveTwoBoneIK(pose.HipR, new Vec2(4.2 * s, 0), thighLen, shinLen, kneeBend);

                pose.ShoulderL = pose.Neck + new Vec2(-2.0 * s, 2.2 * s);
                pose.ShoulderR = pose.Neck + new Vec2(2.0 * s, 2.2 * s);
                Vec2 handLTarget = character.State == StickmanState.LookAround
                    ? pose.HeadCenter + new Vec2(-6 * s, 2 * s)
                    : pose.ShoulderL + new Vec2(-4.8 * s + sway * 0.5, 14.5 * s);
                Vec2 handRTarget = pose.ShoulderR + new Vec2(4.8 * s + sway * 0.5, 14.5 * s);

                (pose.ElbowL, pose.HandL) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderL, handLTarget, upperArmLen, foreArmLen, -elbowBend);
                (pose.ElbowR, pose.HandR) = SkeletonRig.SolveTwoBoneIK(pose.ShoulderR, handRTarget, upperArmLen, foreArmLen, elbowBend);

                pose.EyeExpression = character.BlinkTimer > 0 ? EyeExpression.Blink : EyeExpression.Normal;
                break;
            }
        }

        if (character.BlinkTimer > 0 && pose.EyeExpression == EyeExpression.Normal)
        {
            pose.EyeExpression = EyeExpression.Blink;
        }

        return pose;
    }
}
