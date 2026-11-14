using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DesktopStickmanUniverse.Animation;
using DesktopStickmanUniverse.Desktop;
using DesktopStickmanUniverse.Physics;

namespace DesktopStickmanUniverse.Rendering;

/// <summary>
/// High-contrast, DPI-aware vector renderer for stickman skeletons,
/// expressive eyes, surface shadows, emotes, and particle effects.
/// </summary>
public sealed class CharacterRenderer
{
    private readonly Dictionary<Color, PenPair> _penCache = new();
    private readonly Dictionary<Color, SolidColorBrush> _brushCache = new();
    private readonly Typeface _emoteTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly SolidColorBrush _shadowBrush;
    private readonly SolidColorBrush _eyeDarkBrush;
    private readonly SolidColorBrush _eyeWhiteBrush;
    private readonly Pen _hoverRingPen;
    private readonly SolidColorBrush _hitCircleBrush;

    private sealed record PenPair(Pen OutlinePen, Pen BodyPen, Pen EyeLinePen);

    public CharacterRenderer()
    {
        _shadowBrush = CreateFrozenBrush(Color.FromArgb(72, 8, 12, 20));
        _eyeDarkBrush = CreateFrozenBrush(Color.FromRgb(18, 22, 32));
        _eyeWhiteBrush = CreateFrozenBrush(Color.FromRgb(250, 252, 255));
        _hitCircleBrush = CreateFrozenBrush(Color.FromArgb(1, 0, 0, 0));

        _hoverRingPen = new Pen(CreateFrozenBrush(Color.FromArgb(135, 255, 255, 255)), 1.4)
        {
            DashStyle = new DashStyle(new[] { 3.0, 3.0 }, 0)
        };
        _hoverRingPen.Freeze();
    }

    public void RenderMonitor(
        OverlayCanvasElement canvas,
        MonitorInfoData monitor,
        double dpiScaleX,
        double dpiScaleY,
        IReadOnlyList<StickmanCharacter> characters,
        IReadOnlyList<ParticleEffect> particles,
        int? hoveredCharacterId,
        bool showParticles)
    {
        using var dc = canvas.RenderOpen();

        if (characters.Count == 0 && (!showParticles || particles.Count == 0))
        {
            return;
        }

        double marginPhys = 90.0 * Math.Max(dpiScaleX, dpiScaleY);
        var expandedBounds = new PixelRect(
            monitor.Bounds.Left - marginPhys,
            monitor.Bounds.Top - marginPhys,
            monitor.Bounds.Right + marginPhys,
            monitor.Bounds.Bottom + marginPhys);

        // Render particles behind/around characters.
        if (showParticles)
        {
            foreach (var p in particles)
            {
                if (!expandedBounds.Contains(p.X, p.Y))
                {
                    continue;
                }

                double lx = (p.X - monitor.Bounds.Left) / dpiScaleX;
                double ly = (p.Y - monitor.Bounds.Top) / dpiScaleY;
                double progress = Math.Clamp(p.Life / Math.Max(0.05, p.MaxLife), 0.0, 1.0);
                double alphaRatio = 1.0 - progress;
                byte alpha = (byte)(p.Color.A * alphaRatio);
                var brush = new SolidColorBrush(Color.FromArgb(alpha, p.Color.R, p.Color.G, p.Color.B));
                brush.Freeze();

                if (p.Kind == ParticleKind.Text || !string.IsNullOrEmpty(p.Text))
                {
                    var ft = new FormattedText(
                        p.Text ?? string.Empty,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        _emoteTypeface,
                        Math.Max(10.0, p.Size / dpiScaleX),
                        brush,
                        dpiScaleX);
                    dc.DrawText(ft, new Point(lx - ft.Width * 0.5, ly - ft.Height * 0.5));
                }
                else if (p.Kind == ParticleKind.Ring)
                {
                    double startR = p.Size / dpiScaleX;
                    double endR = Math.Max(startR + 8.0, p.EndSize / dpiScaleX);
                    double r = startR + (endR - startR) * progress;
                    var ringPen = new Pen(brush, Math.Max(1.0, 2.4 * alphaRatio));
                    ringPen.Freeze();
                    dc.DrawEllipse(null, ringPen, new Point(lx, ly), r, r);
                }
                else if (p.Kind == ParticleKind.Star)
                {
                    double r = (p.Size / dpiScaleX) * (0.4 + 0.8 * alphaRatio);
                    var starPen = new Pen(brush, 1.8)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round
                    };
                    starPen.Freeze();
                    dc.DrawLine(starPen, new Point(lx - r, ly), new Point(lx + r, ly));
                    dc.DrawLine(starPen, new Point(lx, ly - r), new Point(lx, ly + r));
                    double dr = r * 0.65;
                    dc.DrawLine(starPen, new Point(lx - dr, ly - dr), new Point(lx + dr, ly + dr));
                    dc.DrawLine(starPen, new Point(lx - dr, ly + dr), new Point(lx + dr, ly - dr));
                }
                else
                {
                    double r = (p.Size / dpiScaleX) * (0.5 + 0.5 * alphaRatio);
                    dc.DrawEllipse(brush, null, new Point(lx, ly), r, r);
                }
            }
        }

        // Render characters visible on this monitor.
        foreach (var c in characters)
        {
            if (!expandedBounds.Contains(c.X, c.Y))
            {
                continue;
            }

            double lx = (c.X - monitor.Bounds.Left) / dpiScaleX;
            double ly = (c.Y - monitor.Bounds.Top) / dpiScaleY;
            bool isHovered = hoveredCharacterId == c.Id;

            DrawCharacter(dc, c, lx, ly, dpiScaleX, isHovered);
        }
    }

    private void DrawCharacter(
        DrawingContext dc,
        StickmanCharacter c,
        double anchorX,
        double anchorY,
        double pixelsPerDip,
        bool isHovered)
    {
        var pose = c.CurrentPose;
        double s = c.VisualScale;

        // Subtle near-invisible hit backing ensures clicking a character never misses between thin limbs
        // when WS_EX_TRANSPARENT is lifted on hover.
        dc.DrawEllipse(_hitCircleBrush, isHovered ? _hoverRingPen : null, new Point(anchorX, anchorY - 25 * s), 18 * s, 28 * s);

        // Ground shadow when grounded or near ground.
        if (c.IsGrounded && c.State != StickmanState.Climb)
        {
            dc.DrawEllipse(_shadowBrush, null, new Point(anchorX, anchorY + 0.5), 9.5 * s, 2.4 * s);
        }

        var pens = GetOrCreatePens(c.PrimaryColor, s);
        var headBrush = GetOrCreateBrush(c.PrimaryColor);

        Point P(Vec2 v) => new(anchorX + v.X, anchorY + v.Y);

        // Two-pass limb stroke: subtle dark outline halo first, then crisp vibrant color stroke.
        DrawSkeletonPass(dc, pose, pens.OutlinePen, P);
        DrawSkeletonPass(dc, pose, pens.BodyPen, P);

        // Head circle with contrasting outer ring + colored core.
        Point headPt = P(pose.HeadCenter);
        dc.DrawEllipse(headBrush, pens.OutlinePen, headPt, pose.HeadRadius, pose.HeadRadius);
        dc.DrawEllipse(headBrush, null, headPt, pose.HeadRadius, pose.HeadRadius);

        DrawEyes(dc, pose, headPt, pens.EyeLinePen, s);

        // Floating emote (!, ?, Zzz).
        if (!string.IsNullOrEmpty(c.EmoteText) && c.EmoteTimer > 0)
        {
            double fade = Math.Clamp(c.EmoteTimer / 0.35, 0.0, 1.0);
            var bg = new SolidColorBrush(Color.FromArgb((byte)(210 * fade), 18, 22, 32));
            var fg = new SolidColorBrush(Color.FromArgb((byte)(255 * fade), 255, 255, 255));
            bg.Freeze();
            fg.Freeze();

            var ft = new FormattedText(
                c.EmoteText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _emoteTypeface,
                11.0 * s,
                fg,
                pixelsPerDip);

            double bx = headPt.X - ft.Width * 0.5 - 5;
            double by = headPt.Y - pose.HeadRadius - ft.Height - 9;
            dc.DrawRoundedRectangle(bg, null, new Rect(bx, by, ft.Width + 10, ft.Height + 3), 5, 5);
            dc.DrawText(ft, new Point(bx + 5, by + 1));
        }
    }

    private static void DrawSkeletonPass(
        DrawingContext dc,
        StickmanPose pose,
        Pen pen,
        Func<Vec2, Point> toPoint)
    {
        // Back limbs first, then torso, then front limbs.
        dc.DrawLine(pen, toPoint(pose.HipL), toPoint(pose.KneeL));
        dc.DrawLine(pen, toPoint(pose.KneeL), toPoint(pose.FootL));

        dc.DrawLine(pen, toPoint(pose.ShoulderL), toPoint(pose.ElbowL));
        dc.DrawLine(pen, toPoint(pose.ElbowL), toPoint(pose.HandL));

        dc.DrawLine(pen, toPoint(pose.Pelvis), toPoint(pose.Chest));
        dc.DrawLine(pen, toPoint(pose.Chest), toPoint(pose.Neck));

        dc.DrawLine(pen, toPoint(pose.HipR), toPoint(pose.KneeR));
        dc.DrawLine(pen, toPoint(pose.KneeR), toPoint(pose.FootR));

        dc.DrawLine(pen, toPoint(pose.ShoulderR), toPoint(pose.ElbowR));
        dc.DrawLine(pen, toPoint(pose.ElbowR), toPoint(pose.HandR));
    }

    private void DrawEyes(DrawingContext dc, StickmanPose pose, Point headPt, Pen linePen, double s)
    {
        double f = pose.Facing >= 0 ? 1.0 : -1.0;
        double lookX = Math.Clamp(pose.EyeLookOffset.X, -1.6, 1.6) * s;
        double lookY = Math.Clamp(pose.EyeLookOffset.Y, -1.2, 1.2) * s;

        Point leftEye = new(headPt.X + f * 1.1 * s - 1.7 * s + lookX, headPt.Y - 0.6 * s + lookY);
        Point rightEye = new(headPt.X + f * 1.1 * s + 1.8 * s + lookX, headPt.Y - 0.6 * s + lookY);

        switch (pose.EyeExpression)
        {
            case EyeExpression.Blink:
            case EyeExpression.Sleep:
                dc.DrawLine(linePen, new Point(leftEye.X - 1.3 * s, leftEye.Y), new Point(leftEye.X + 1.3 * s, leftEye.Y));
                dc.DrawLine(linePen, new Point(rightEye.X - 1.3 * s, rightEye.Y), new Point(rightEye.X + 1.3 * s, rightEye.Y));
                break;

            case EyeExpression.Happy:
                dc.DrawLine(linePen, new Point(leftEye.X - 1.2 * s, leftEye.Y + 0.6 * s), new Point(leftEye.X, leftEye.Y - 0.9 * s));
                dc.DrawLine(linePen, new Point(leftEye.X, leftEye.Y - 0.9 * s), new Point(leftEye.X + 1.2 * s, leftEye.Y + 0.6 * s));
                dc.DrawLine(linePen, new Point(rightEye.X - 1.2 * s, rightEye.Y + 0.6 * s), new Point(rightEye.X, rightEye.Y - 0.9 * s));
                dc.DrawLine(linePen, new Point(rightEye.X, rightEye.Y - 0.9 * s), new Point(rightEye.X + 1.2 * s, rightEye.Y + 0.6 * s));
                break;

            case EyeExpression.Dizzy:
                dc.DrawLine(linePen, new Point(leftEye.X - 1.1 * s, leftEye.Y - 1.1 * s), new Point(leftEye.X + 1.1 * s, leftEye.Y + 1.1 * s));
                dc.DrawLine(linePen, new Point(leftEye.X + 1.1 * s, leftEye.Y - 1.1 * s), new Point(leftEye.X - 1.1 * s, leftEye.Y + 1.1 * s));
                dc.DrawLine(linePen, new Point(rightEye.X - 1.1 * s, rightEye.Y - 1.1 * s), new Point(rightEye.X + 1.1 * s, rightEye.Y + 1.1 * s));
                dc.DrawLine(linePen, new Point(rightEye.X + 1.1 * s, rightEye.Y - 1.1 * s), new Point(rightEye.X - 1.1 * s, rightEye.Y + 1.1 * s));
                break;

            case EyeExpression.Surprised:
                dc.DrawEllipse(_eyeWhiteBrush, null, leftEye, 1.55 * s, 1.85 * s);
                dc.DrawEllipse(_eyeWhiteBrush, null, rightEye, 1.55 * s, 1.85 * s);
                dc.DrawEllipse(_eyeDarkBrush, null, leftEye, 0.75 * s, 0.9 * s);
                dc.DrawEllipse(_eyeDarkBrush, null, rightEye, 0.75 * s, 0.9 * s);
                break;

            case EyeExpression.Determined:
                dc.DrawEllipse(_eyeWhiteBrush, null, leftEye, 1.25 * s, 1.45 * s);
                dc.DrawEllipse(_eyeWhiteBrush, null, rightEye, 1.25 * s, 1.45 * s);
                dc.DrawEllipse(_eyeDarkBrush, null, new Point(leftEye.X + f * 0.35 * s, leftEye.Y), 0.75 * s, 0.85 * s);
                dc.DrawEllipse(_eyeDarkBrush, null, new Point(rightEye.X + f * 0.35 * s, rightEye.Y), 0.75 * s, 0.85 * s);
                dc.DrawLine(linePen, new Point(leftEye.X - 1.5 * s, leftEye.Y - 1.8 * s), new Point(leftEye.X + 1.4 * s, leftEye.Y - 1.0 * s));
                dc.DrawLine(linePen, new Point(rightEye.X + 1.5 * s, rightEye.Y - 1.8 * s), new Point(rightEye.X - 1.4 * s, rightEye.Y - 1.0 * s));
                break;

            default:
                dc.DrawEllipse(_eyeWhiteBrush, null, leftEye, 1.25 * s, 1.55 * s);
                dc.DrawEllipse(_eyeWhiteBrush, null, rightEye, 1.25 * s, 1.55 * s);
                dc.DrawEllipse(_eyeDarkBrush, null, new Point(leftEye.X + f * 0.3 * s, leftEye.Y), 0.72 * s, 0.85 * s);
                dc.DrawEllipse(_eyeDarkBrush, null, new Point(rightEye.X + f * 0.3 * s, rightEye.Y), 0.72 * s, 0.85 * s);
                break;
        }
    }

    private PenPair GetOrCreatePens(Color color, double scale)
    {
        // Bucket by color; if scale varies significantly per character, compute directly or key by color.
        if (_penCache.TryGetValue(color, out var cached) && Math.Abs(scale - 1.0) < 0.08)
        {
            return cached;
        }

        double bodyThickness = 2.9 * scale;
        double outlineThickness = 4.9 * scale;

        var outlineBrush = CreateFrozenBrush(Color.FromArgb(145, 10, 14, 22));
        var outlinePen = new Pen(outlineBrush, outlineThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        outlinePen.Freeze();

        var bodyBrush = GetOrCreateBrush(color);
        var bodyPen = new Pen(bodyBrush, bodyThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        bodyPen.Freeze();

        var eyePen = new Pen(_eyeDarkBrush, Math.Max(1.0, 1.2 * scale))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        eyePen.Freeze();

        var pair = new PenPair(outlinePen, bodyPen, eyePen);
        if (Math.Abs(scale - 1.0) < 0.08)
        {
            _penCache[color] = pair;
        }

        return pair;
    }

    private SolidColorBrush GetOrCreateBrush(Color color)
    {
        if (_brushCache.TryGetValue(color, out var brush))
        {
            return brush;
        }

        brush = CreateFrozenBrush(color);
        _brushCache[color] = brush;
        return brush;
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }
}
