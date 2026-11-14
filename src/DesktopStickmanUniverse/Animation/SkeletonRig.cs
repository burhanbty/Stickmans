namespace DesktopStickmanUniverse.Animation;

public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 Zero => new(0, 0);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public Vec2 Normalized()
    {
        double len = Length;
        return len > 1e-6 ? new Vec2(X / len, Y / len) : Zero;
    }

    public static Vec2 Lerp(Vec2 a, Vec2 b, double t)
        => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);
}

public enum EyeExpression
{
    Normal,
    Blink,
    Sleep,
    Surprised,
    Determined,
    Happy,
    Dizzy
}

/// <summary>
/// Articulated 2D stick-figure pose expressed in local DIP coordinates
/// where (0, 0) is the character's ground/feet anchor and negative Y is upward.
/// </summary>
public sealed class StickmanPose
{
    public Vec2 Pelvis { get; set; }
    public Vec2 Chest { get; set; }
    public Vec2 Neck { get; set; }
    public Vec2 HeadCenter { get; set; }
    public double HeadRadius { get; set; } = 6.8;

    public Vec2 ShoulderL { get; set; }
    public Vec2 ElbowL { get; set; }
    public Vec2 HandL { get; set; }

    public Vec2 ShoulderR { get; set; }
    public Vec2 ElbowR { get; set; }
    public Vec2 HandR { get; set; }

    public Vec2 HipL { get; set; }
    public Vec2 KneeL { get; set; }
    public Vec2 FootL { get; set; }

    public Vec2 HipR { get; set; }
    public Vec2 KneeR { get; set; }
    public Vec2 FootR { get; set; }

    public Vec2 EyeLookOffset { get; set; }
    public EyeExpression EyeExpression { get; set; } = EyeExpression.Normal;
    public int Facing { get; set; } = 1;

    public StickmanPose Clone() => new()
    {
        Pelvis = Pelvis,
        Chest = Chest,
        Neck = Neck,
        HeadCenter = HeadCenter,
        HeadRadius = HeadRadius,
        ShoulderL = ShoulderL,
        ElbowL = ElbowL,
        HandL = HandL,
        ShoulderR = ShoulderR,
        ElbowR = ElbowR,
        HandR = HandR,
        HipL = HipL,
        KneeL = KneeL,
        FootL = FootL,
        HipR = HipR,
        KneeR = KneeR,
        FootR = FootR,
        EyeLookOffset = EyeLookOffset,
        EyeExpression = EyeExpression,
        Facing = Facing
    };

    public void BlendFrom(StickmanPose target, double alpha)
    {
        double t = Math.Clamp(alpha, 0.0, 1.0);
        Pelvis = Vec2.Lerp(Pelvis, target.Pelvis, t);
        Chest = Vec2.Lerp(Chest, target.Chest, t);
        Neck = Vec2.Lerp(Neck, target.Neck, t);
        HeadCenter = Vec2.Lerp(HeadCenter, target.HeadCenter, t);
        HeadRadius = HeadRadius + (target.HeadRadius - HeadRadius) * t;

        ShoulderL = Vec2.Lerp(ShoulderL, target.ShoulderL, t);
        ElbowL = Vec2.Lerp(ElbowL, target.ElbowL, t);
        HandL = Vec2.Lerp(HandL, target.HandL, t);

        ShoulderR = Vec2.Lerp(ShoulderR, target.ShoulderR, t);
        ElbowR = Vec2.Lerp(ElbowR, target.ElbowR, t);
        HandR = Vec2.Lerp(HandR, target.HandR, t);

        HipL = Vec2.Lerp(HipL, target.HipL, t);
        KneeL = Vec2.Lerp(KneeL, target.KneeL, t);
        FootL = Vec2.Lerp(FootL, target.FootL, t);

        HipR = Vec2.Lerp(HipR, target.HipR, t);
        KneeR = Vec2.Lerp(KneeR, target.KneeR, t);
        FootR = Vec2.Lerp(FootR, target.FootR, t);

        EyeLookOffset = Vec2.Lerp(EyeLookOffset, target.EyeLookOffset, t);
        EyeExpression = target.EyeExpression;
        Facing = target.Facing;
    }

    /// <summary>
    /// Rotates every joint in the pose around a local pivot point (used for flips and acrobatic lunges).
    /// </summary>
    public void RotateAround(Vec2 pivot, double angleRadians)
    {
        if (Math.Abs(angleRadians) < 1e-5)
        {
            return;
        }

        double cos = Math.Cos(angleRadians);
        double sin = Math.Sin(angleRadians);

        Vec2 Rot(Vec2 p)
        {
            double dx = p.X - pivot.X;
            double dy = p.Y - pivot.Y;
            return new Vec2(pivot.X + dx * cos - dy * sin, pivot.Y + dx * sin + dy * cos);
        }

        Pelvis = Rot(Pelvis);
        Chest = Rot(Chest);
        Neck = Rot(Neck);
        HeadCenter = Rot(HeadCenter);

        ShoulderL = Rot(ShoulderL);
        ElbowL = Rot(ElbowL);
        HandL = Rot(HandL);

        ShoulderR = Rot(ShoulderR);
        ElbowR = Rot(ElbowR);
        HandR = Rot(HandR);

        HipL = Rot(HipL);
        KneeL = Rot(KneeL);
        FootL = Rot(FootL);

        HipR = Rot(HipR);
        KneeR = Rot(KneeR);
        FootR = Rot(FootR);
    }

    public static StickmanPose CreateNeutral(double scale = 1.0)
    {
        var pose = new StickmanPose
        {
            Pelvis = new Vec2(0, -24 * scale),
            Chest = new Vec2(0, -34 * scale),
            Neck = new Vec2(0, -40 * scale),
            HeadCenter = new Vec2(0, -47 * scale),
            HeadRadius = 6.8 * scale,
            ShoulderL = new Vec2(-2.2 * scale, -38 * scale),
            ElbowL = new Vec2(-6 * scale, -29 * scale),
            HandL = new Vec2(-7 * scale, -20 * scale),
            ShoulderR = new Vec2(2.2 * scale, -38 * scale),
            ElbowR = new Vec2(6 * scale, -29 * scale),
            HandR = new Vec2(7 * scale, -20 * scale),
            HipL = new Vec2(-1.8 * scale, -24 * scale),
            KneeL = new Vec2(-3.5 * scale, -12 * scale),
            FootL = new Vec2(-4 * scale, 0),
            HipR = new Vec2(1.8 * scale, -24 * scale),
            KneeR = new Vec2(3.5 * scale, -12 * scale),
            FootR = new Vec2(4 * scale, 0)
        };
        return pose;
    }
}

/// <summary>
/// Analytical 2-bone Inverse Kinematics solver for natural knee and elbow bending.
/// </summary>
public static class SkeletonRig
{
    public static (Vec2 MidJoint, Vec2 EndEffector) SolveTwoBoneIK(
        Vec2 root,
        Vec2 target,
        double upperLength,
        double lowerLength,
        double bendSign)
    {
        Vec2 diff = target - root;
        double dist = diff.Length;

        double minDist = Math.Abs(upperLength - lowerLength) + 0.05;
        double maxDist = (upperLength + lowerLength) - 0.02;

        if (dist < 1e-5)
        {
            Vec2 fallbackDir = new(0, 1);
            Vec2 mid = root + fallbackDir * upperLength;
            Vec2 end = mid + fallbackDir * lowerLength;
            return (mid, end);
        }

        double clampedDist = Math.Clamp(dist, minDist, maxDist);
        Vec2 dir = diff / dist;
        Vec2 clampedEnd = root + dir * clampedDist;

        // Law of cosines for the triangle (upperLength, lowerLength, clampedDist).
        double cosAngle = (upperLength * upperLength + clampedDist * clampedDist - lowerLength * lowerLength)
                          / (2.0 * upperLength * clampedDist);
        cosAngle = Math.Clamp(cosAngle, -1.0, 1.0);
        double sinAngle = Math.Sqrt(Math.Max(0.0, 1.0 - cosAngle * cosAngle));

        // Perpendicular vector rotated according to bendSign (+1 or -1).
        Vec2 perp = new(-dir.Y * bendSign, dir.X * bendSign);
        Vec2 midJoint = root + dir * (upperLength * cosAngle) + perp * (upperLength * sinAngle);

        return (midJoint, clampedEnd);
    }
}
