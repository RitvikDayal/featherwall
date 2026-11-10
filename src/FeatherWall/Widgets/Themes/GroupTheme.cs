using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using FeatherWall.Config;

namespace FeatherWall.Widgets.Themes;

/// <summary>Everything a group theme needs to lay out and paint one moment.
///
/// <paramref name="Track"/> is null when there is no record to show — switched off, no now-playing
/// source, or nothing playing or paused. <paramref name="Battery"/> is null the same way. The theme
/// collapses what is absent rather than drawing empty rings. <paramref name="WorkArea"/> is relative
/// to the monitor's top-left, as overlay offsets are.</summary>
public sealed record GroupInput(
    DateTime Now, ClockConfig Clock, DiscConfig Disc, HaloConfig Halo,
    NowPlayingReading? Track, BatteryReading? Battery,
    Color Accent, float Scale, float Dpi, ClockAnchor? Position, Rectangle WorkArea, int MaxCharacters);

/// <summary>One surface of a group theme. <paramref name="FaceBox"/>, relative to the panel, is where
/// the turning record goes on its own layer above the panel; empty when the panel has none.
/// <paramref name="Paint"/> draws everything else, measured by the same call that built it.</summary>
public sealed record GroupPanel(string Key, Rectangle Bounds, Rectangle FaceBox, Action<Graphics> Paint);

/// <summary>Orbit, Poster and Glass dock: the set placed as a group at one position.</summary>
public interface IGroupTheme
{
    ClockAnchor DefaultPosition { get; }
    IReadOnlyList<GroupPanel> Layout(GroupInput input);
}

public static class GroupThemes
{
    /// <summary>The mockups were authored on a 1920×1080 scene where Editorial's clock was 210 units;
    /// on the desktop that clock is 110 px. One factor maps every theme, so they stay in proportion.</summary>
    public const float MockupUnit = 110f / 210f;

    public static IGroupTheme For(WidgetTheme theme) => theme switch
    {
        WidgetTheme.Orbit => Orbit.Instance,
        WidgetTheme.Poster => Poster.Instance,
        WidgetTheme.GlassDock => GlassDock.Instance,
        _ => throw new NotSupportedException($"{theme} is not a group theme"),
    };

    /// <summary>The record is shown exactly when the per-widget overlay would show it.</summary>
    public static NowPlayingReading? TrackFor(DiscConfig disc, bool hasSource, NowPlayingReading reading) =>
        hasSource && !DiscRenderer.Measure(disc, reading).IsEmpty ? reading : null;

    public static BatteryReading? BatteryFor(HaloConfig halo, bool hasSource, BatteryReading reading) =>
        halo.Enabled && hasSource && reading.State != BatteryState.None ? reading : null;

    // ---- shared drawing ------------------------------------------------------------------------

    internal static int Pad(float scale) => (int)Math.Ceiling(ShadowRecipe.Default.Pad * Math.Max(scale, 0.05f));

    /// <summary>A size from the mockup, in its units, at this scale.</summary>
    internal static float U(float mockupUnits, float scale) => mockupUnits * MockupUnit * scale;

    /// <summary>A text size from the mockup, lifted to 11 px at scale 1 where it would fall under.</summary>
    internal static float Text(float mockupUnits, float scale) => Math.Max(mockupUnits * MockupUnit, 11f) * scale;

    /// <summary>Monitor-relative top-left for a block at an anchor, inset by the margin.</summary>
    internal static Point Place(Rectangle work, Size size, ClockAnchor anchor, int margin)
    {
        int col = (int)anchor % 3, row = (int)anchor / 3;
        int x = col switch
        {
            0 => work.Left + margin,
            1 => work.Left + (work.Width - size.Width) / 2,
            _ => work.Right - margin - size.Width,
        };
        int y = row switch
        {
            0 => work.Top + margin,
            1 => work.Top + (work.Height - size.Height) / 2,
            _ => work.Bottom - margin - size.Height,
        };
        return new Point(x, y);
    }

    internal static int Margin(GroupInput input) => (int)Math.Round(48 * Math.Max(input.Dpi, 0.1f));

    /// <summary>Cut to fit <paramref name="maxWidth"/>, ending in an ellipsis. Tracked text is
    /// measured the way it is drawn.</summary>
    public static string FitText(Graphics g, string text, Font font, float maxWidth, float tracking = 0f)
    {
        text = ThemeFonts.Plain(text);
        if (ThemeFonts.MeasureTracked(g, text, font, tracking).Width <= maxWidth) return text;
        for (int n = text.Length - 1; n > 0; n--)
        {
            // Never between the halves of a surrogate pair: half an emoji draws as a box.
            if (char.IsHighSurrogate(text[n - 1])) continue;
            var candidate = text[..n].TrimEnd() + "…";
            if (ThemeFonts.MeasureTracked(g, candidate, font, tracking).Width <= maxWidth) return candidate;
        }
        return "…";
    }

    /// <summary>A ring with an arc: the dial, the record's rim and the battery all use it.</summary>
    internal static void Ring(Graphics g, RectangleF ring, float stroke, Color fill, Color track,
                              Color arc, float sweep, float arcStroke = 0f)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (fill.A > 0)
            using (var brush = new SolidBrush(fill)) g.FillEllipse(brush, ring);
        using (var pen = new Pen(track, stroke)) g.DrawEllipse(pen, ring);
        if (sweep <= 0) return;
        using var arcPen = new Pen(arc, arcStroke > 0 ? arcStroke : stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(arcPen, ring, -90f, Math.Min(sweep, 360f));
    }

    /// <summary>The battery's number and sign, or a tick when full, with the bolt while charging —
    /// the states keep their meaning in every theme.</summary>
    internal static void BatteryCentre(Graphics g, PointF centre, float side, BatteryReading r, Color colour,
                                       string[] family, float numberPx, float percentPx)
    {
        using var ink = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
        if (r.State == BatteryState.Charged)
        {
            using var tickFont = ThemeFonts.Create(ThemeFonts.TextSemibold, numberPx);
            var t = g.MeasureString("✓", tickFont, PointF.Empty, ThemeFonts.Tight);
            g.DrawString("✓", tickFont, ink, centre.X - t.Width / 2f, centre.Y - t.Height / 2f, ThemeFonts.Tight);
            return;
        }
        string number = r.Percent.ToString(CultureInfo.InvariantCulture);
        using var numberFont = ThemeFonts.Create(family, numberPx);
        using var percentFont = ThemeFonts.Create(family, percentPx);
        var ns = g.MeasureString(number, numberFont, PointF.Empty, ThemeFonts.Tight);
        var ps = g.MeasureString("%", percentFont, PointF.Empty, ThemeFonts.Tight);
        bool charging = r.State == BatteryState.Charging;
        float boltW = charging ? side * 0.10f : 0f, boltGap = charging ? side * 0.03f : 0f;
        float left = centre.X - (boltW + boltGap + ns.Width + ps.Width) / 2f;
        if (charging) BatteryHaloRenderer.PaintBolt(g, left + boltW / 2f, centre.Y, side * 0.72f, colour);
        float x = left + boltW + boltGap;
        g.DrawString(number, numberFont, ink, x, centre.Y - ns.Height / 2f, ThemeFonts.Tight);
        using var dim = new SolidBrush(Color.FromArgb(185, 255, 255, 255));
        g.DrawString("%", percentFont, dim, x + ns.Width, centre.Y + ns.Height / 2f - ps.Height, ThemeFonts.Tight);
    }

    internal static Color Alpha(Color c, float alpha) => Color.FromArgb((int)Math.Round(255 * Math.Clamp(alpha, 0f, 1f)), c);
}
