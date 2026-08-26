using System.Drawing;
using System.Drawing.Drawing2D;
using FeatherWall.Config;

namespace FeatherWall.Widgets;

/// <summary>Draws the battery halo: a track ring, an arc at the charge level, and a centre glyph.
///
/// Colour and size are pure so the whole ramp is testable; the painting itself is verified by
/// looking at the desktop, because asserting on GDI+ output would only re-implement GDI+.
///
/// Nothing here moves. The arc is the level, which is the reason this concept was chosen over the
/// animated ones — it carries its information without a frame clock.</summary>
public static class BatteryHaloRenderer
{
    /// <summary>Below this a ring is thinner than a pixel and simply vanishes, so a nonsensical
    /// size is clamped rather than drawn as nothing.</summary>
    private const int MinSize = 12;

    /// <summary>A step, not a blend. Thresholds are inclusive upper bounds, and low wins any
    /// overlap an inverted config creates rather than throwing — a config written wrong should
    /// not stop the wallpaper starting, the same rule the Sources list follows.</summary>
    public static Color ColorFor(int percent, BatteryState state, HaloConfig config)
    {
        if (state == BatteryState.Charged) return ClockRenderer.ParseColor(config.ChargedColor);
        if (!config.ColorByLevel) return ClockRenderer.ParseColor(config.HighColor);
        if (percent <= config.LowThreshold) return ClockRenderer.ParseColor(config.LowColor);
        if (percent <= config.MidThreshold) return ClockRenderer.ParseColor(config.MidColor);
        return ClockRenderer.ParseColor(config.HighColor);
    }

    /// <summary>Empty means "draw nothing" — no battery, or switched off. The overlay reads an
    /// empty size as no halo rather than as a zero-sized one.</summary>
    public static Size Measure(HaloConfig config, BatteryReading reading, float scale = 1f)
    {
        if (!config.Enabled || reading.State == BatteryState.None) return Size.Empty;
        int side = Math.Max((int)Math.Round(config.Size * Math.Max(scale, 0f)), MinSize);
        return new Size(side, side);
    }

    public static void Paint(Graphics g, Rectangle box, HaloConfig config, BatteryReading reading)
    {
        if (box.Width <= 0 || box.Height <= 0 || reading.State == BatteryState.None) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        var colour = ColorFor(reading.Percent, reading.State, config);
        float side = Math.Min(box.Width, box.Height);
        float stroke = Math.Max(side * 0.10f, 1.5f);
        float pad = stroke * 1.6f;                    // room for the glow to fall off inside the box
        var ring = new RectangleF(box.X + pad, box.Y + pad, side - pad * 2, side - pad * 2);
        if (ring.Width <= 0 || ring.Height <= 0) return;

        float cx = ring.X + ring.Width / 2f, cy = ring.Y + ring.Height / 2f;

        // Glow, faked with a radial gradient the way the tray icon already does it — GDI+ has no
        // blur, and a PathGradientBrush falloff is indistinguishable at this size.
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(box.X, box.Y, side, side);
            using var glow = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(colour.A / 3, colour),
                SurroundColors = [Color.FromArgb(0, colour)],
                CenterPoint = new PointF(cx, cy),
            };
            g.FillPath(glow, path);
        }

        using (var trackPen = new Pen(ClockRenderer.ParseColor(config.TrackColor), stroke))
            g.DrawEllipse(trackPen, ring);

        float sweep = reading.State == BatteryState.Charged
            ? 360f
            : 360f * Math.Clamp(reading.Percent, 0, 100) / 100f;
        if (sweep > 0)
        {
            using var arcPen = new Pen(colour, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(arcPen, ring, -90f, sweep);
        }

        PaintGlyph(g, cx, cy, side, stroke, colour, reading.State, reading.Percent);
    }

    /// <summary>The centre carries the charge: the percentage, with a bolt beside it while
    /// charging. Charged shows a tick instead — the ring is already full and "100" adds nothing.
    ///
    /// Geometry is written against a unit derived from the ring size, so one config value scales
    /// the whole thing rather than a set of hand-tuned constants per size.</summary>
    private static void PaintGlyph(Graphics g, float cx, float cy, float side, float stroke,
                                   Color colour, BatteryState state, int percent)
    {
        float u = side / 34f;
        using var brush = new SolidBrush(colour);

        if (state == BatteryState.Charged)
        {
            using var pen = new Pen(colour, 2.1f * u)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            g.DrawLines(pen,
            [
                new PointF(cx - 4.5f * u, cy + 0.5f * u),
                new PointF(cx - 1.3f * u, cy + 3.8f * u),
                new PointF(cx + 5.0f * u, cy - 3.6f * u),
            ]);
            return;
        }

        bool charging = state == BatteryState.Charging;
        string number = percent.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // How much horizontal room there is inside the ring, measured rather than guessed. Taking
        // 88% of the inner diameter keeps the glyphs off the curve, where a straight line of text
        // runs out of height before it runs out of width.
        float innerRadius = side / 2f - stroke * 1.9f;
        float available = Math.Max(innerRadius * 2f * 0.88f, 1f);

        float boltW = charging ? 4.6f * u : 0f;
        float gap = charging ? 1.6f * u : 0f;

        // Fit by measuring and shrinking, not by a per-digit-count guess. "100 charging" at 34 px
        // rendered as "10c" under a fixed size — a number clipped by its own ring is worse than a
        // smaller number.
        float fontSize = side * 0.31f;
        Font font = new(FontFamily.GenericSansSerif, fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        SizeF textSize = g.MeasureString(number, font);
        if (textSize.Width + boltW + gap > available)
        {
            float scale = Math.Max((available - boltW - gap) / Math.Max(textSize.Width, 0.01f), 0.35f);
            font.Dispose();
            font = new Font(FontFamily.GenericSansSerif, Math.Max(fontSize * scale, 6f),
                            FontStyle.Regular, GraphicsUnit.Pixel);
            textSize = g.MeasureString(number, font);
        }

        using (font)
        {
            float totalW = textSize.Width + boltW + gap;
            float left = cx - totalW / 2f;
            PaintNumberAndBolt(g, brush, font, number, left, cy, u, boltW, gap, textSize, charging);
        }
    }

    /// <summary>Bolt then number, laid out left to right from an already-centred origin.</summary>
    private static void PaintNumberAndBolt(Graphics g, Brush brush, Font font, string number,
                                           float left, float cy, float u,
                                           float boltW, float gap, SizeF textSize, bool charging)
    {

        if (charging)
        {
            float bx = left + boltW / 2f;
            PointF[] bolt =
            [
                new(bx + 1.4f * u, cy - 5.2f * u), new(bx - 2.4f * u, cy + 0.7f * u),
                new(bx - 0.2f * u, cy + 0.7f * u), new(bx - 1.4f * u, cy + 5.2f * u),
                new(bx + 2.6f * u, cy - 0.7f * u), new(bx + 0.4f * u, cy - 0.7f * u),
            ];
            g.FillPolygon(brush, bolt);
        }

        g.DrawString(number, font, brush, left + boltW + gap, cy - textSize.Height / 2f);
    }
}
