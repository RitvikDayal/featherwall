using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using FeatherWall.Config;

namespace FeatherWall.Widgets;

/// <summary>Draws the battery halo: a thin gauge ring with the charge inside it.
///
/// The geometry is the "Halo" concept from the 2026-08-26 mockup set, kept to its proportions
/// rather than reinterpreted: a ring roughly 7% of its own diameter thick, the percentage set
/// large in the middle with a small per-cent sign beside it, a tick when full, and a soft glow
/// behind. An earlier attempt used a ring 10% thick, which ate the space the number needed and
/// left it illegible on a busy wallpaper.
///
/// Colour and size are pure so the ramp is testable; the painting is verified by rendering it and
/// looking, because asserting on GDI+ output would only re-implement GDI+.</summary>
public static class BatteryHaloRenderer
{
    /// <summary>Below this a ring is thinner than a pixel and simply vanishes, so a nonsensical
    /// size is clamped rather than drawn as nothing.</summary>
    private const int MinSize = 12;

    // Proportions taken from the approved mockup, expressed against the drawn diameter so one
    // config value scales the whole thing.
    private const float StrokeRatio = 0.069f;
    private const float NumberRatio = 0.276f;
    private const float PercentRatio = 0.155f;
    private const float TickRatio = 0.293f;

    /// <summary>GDI+ pads MeasureString for overhang, which pushes the per-cent sign away from the
    /// digits by a few pixels that look like a mistake at this size. Typographic measurement
    /// reports the glyphs' real extent.</summary>
    private static readonly StringFormat Tight = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.MeasureTrailingSpaces,
    };

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

    /// <summary><paramref name="phase"/> advances the comet that chases the arc while charging,
    /// in turns. Zero draws no comet at all, so a caller with no timer gets the whole design
    /// static and costs nothing.</summary>
    public static void Paint(Graphics g, Rectangle box, HaloConfig config, BatteryReading reading,
                             float phase = 0f)
    {
        if (box.Width <= 0 || box.Height <= 0 || reading.State == BatteryState.None) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var colour = ColorFor(reading.Percent, reading.State, config);
        float side = Math.Min(box.Width, box.Height);
        float stroke = Math.Max(side * StrokeRatio, 1.4f);
        float pad = stroke * 1.5f;
        var ring = new RectangleF(box.X + pad, box.Y + pad, side - pad * 2, side - pad * 2);
        if (ring.Width <= 0 || ring.Height <= 0) return;

        float cx = ring.X + ring.Width / 2f, cy = ring.Y + ring.Height / 2f;
        float radius = ring.Width / 2f;

        // Lighter than the mockup's: a canvas radial gradient falls off faster than a
        // PathGradientBrush, so matching the numbers produced a visible bloom on pale wallpaper.
        PaintGlow(g, cx, cy, radius * 1.75f, colour, reading.State == BatteryState.Charging ? 0.17f : 0.11f);

        using (var track = new Pen(ClockRenderer.ParseColor(config.TrackColor), stroke))
            g.DrawEllipse(track, ring);

        float sweep = reading.State == BatteryState.Charged
            ? 360f
            : 360f * Math.Clamp(reading.Percent, 0, 100) / 100f;
        if (sweep > 0)
        {
            using var arc = new Pen(colour, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(arc, ring, -90f, sweep);
        }

        if (reading.State == BatteryState.Charging && phase != 0f)
            PaintComet(g, cx, cy, radius, stroke, phase);

        PaintCentre(g, cx, cy, side, reading.State, reading.Percent);
    }

    /// <summary>A soft falloff behind the ring. GDI+ has no blur, and a PathGradientBrush is
    /// indistinguishable from one at this size — the tray icon already relies on that.</summary>
    private static void PaintGlow(Graphics g, float cx, float cy, float radius, Color colour, float strength)
    {
        if (radius <= 0) return;
        using var path = new GraphicsPath();
        path.AddEllipse(cx - radius, cy - radius, radius * 2, radius * 2);
        using var glow = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb((int)(colour.A * strength), colour),
            SurroundColors = [Color.FromArgb(0, colour)],
            CenterPoint = new PointF(cx, cy),
        };
        g.FillPath(glow, path);
    }

    /// <summary>Nine fading segments trailing a bright head, running round the rim. This is the
    /// only moving part of the widget and it exists only while charging — a state that ends.</summary>
    private static void PaintComet(Graphics g, float cx, float cy, float radius, float stroke, float phase)
    {
        const int Segments = 9;
        float headDeg = -90f + (phase % 1f) * 360f;
        var rect = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);

        for (int i = 0; i < Segments; i++)
        {
            float alpha = (1f - (float)i / Segments) * 0.85f;
            using var pen = new Pen(Color.FromArgb((int)(255 * alpha), Color.White),
                                    Math.Max(stroke * (1f - i * 0.06f), 0.8f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawArc(pen, rect, headDeg - i * 4.2f - 3.4f, 3.4f);
        }

        double rad = headDeg * Math.PI / 180.0;
        PaintGlow(g, cx + (float)Math.Cos(rad) * radius, cy + (float)Math.Sin(rad) * radius,
                  Math.Max(radius * 0.33f, 3f), Color.White, 0.8f);
    }

    /// <summary>The percentage set large with a small per-cent sign beside it, or a tick when the
    /// battery is full — where the ring is already closed and "100" adds nothing.</summary>
    private static void PaintCentre(Graphics g, float cx, float cy, float side, BatteryState state, int percent)
    {
        var ink = Color.FromArgb(242, 246, 255);

        if (state == BatteryState.Charged)
        {
            using var tickFont = new Font("Segoe UI", side * TickRatio, FontStyle.Bold, GraphicsUnit.Pixel);
            DrawCentred(g, "✓", tickFont, ink, cx, cy);
            return;
        }

        string number = percent.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var numberFont = new Font("Segoe UI", side * NumberRatio, FontStyle.Bold, GraphicsUnit.Pixel);
        using var percentFont = new Font("Segoe UI", side * PercentRatio, FontStyle.Bold, GraphicsUnit.Pixel);

        var numberSize = g.MeasureString(number, numberFont, PointF.Empty, Tight);
        var percentSize = g.MeasureString("%", percentFont, PointF.Empty, Tight);

        // Centre the pair, not just the digits, so "8%" and "100%" both sit on the middle.
        float kern = side * 0.022f;
        float totalWidth = numberSize.Width + kern + percentSize.Width;
        float left = cx - totalWidth / 2f;

        DrawShadowed(g, number, numberFont, ink, left, cy - numberSize.Height / 2f);
        DrawShadowed(g, "%", percentFont, Color.FromArgb(185, 240, 246, 255),
                     left + numberSize.Width + kern,
                     cy + numberSize.Height / 2f - percentSize.Height);
    }

    private static void DrawCentred(Graphics g, string text, Font font, Color colour, float cx, float cy)
    {
        var size = g.MeasureString(text, font, PointF.Empty, Tight);
        DrawShadowed(g, text, font, colour, cx - size.Width / 2f, cy - size.Height / 2f);
    }

    /// <summary>White glyphs over a bright wallpaper vanish without this. The clock has always
    /// shadowed its text; the halo's number was the one thing that did not.</summary>
    private static void DrawShadowed(Graphics g, string text, Font font, Color colour, float x, float y)
    {
        using (var shadow = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
            g.DrawString(text, font, shadow, x + 1.3f, y + 1.3f, Tight);
        using var brush = new SolidBrush(colour);
        g.DrawString(text, font, brush, x, y, Tight);
    }
}
