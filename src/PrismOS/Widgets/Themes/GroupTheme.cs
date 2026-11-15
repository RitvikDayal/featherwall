using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using PrismOS.Config;

namespace PrismOS.Widgets.Themes;

/// <summary>Everything a group theme needs to lay out and paint one moment.
///
/// <paramref name="Track"/> is null when there is no record to show — switched off, no now-playing
/// source, or nothing playing or paused. <paramref name="Battery"/> is null the same way. The theme
/// collapses what is absent rather than drawing empty rings. <paramref name="WorkArea"/> is relative
/// to the monitor's top-left, as overlay offsets are. <paramref name="Source"/> is the media app's
/// friendly name (SPOTIFY, CHROME…), null for anything else; <paramref name="TrackArt"/> is the
/// current track's artwork.</summary>
public sealed record GroupInput(
    DateTime Now, ClockConfig Clock, DiscConfig Disc, HaloConfig Halo,
    NowPlayingReading? Track, BatteryReading? Battery,
    Color Accent, float Scale, float Dpi, ClockAnchor? Position, Rectangle WorkArea, int MaxCharacters,
    string? Source = null, Bitmap? TrackArt = null);

/// <summary>One surface of a group theme. <paramref name="FaceBox"/>, relative to the panel, is where
/// the turning record goes on its own layer above the panel; empty when the panel has none.
/// <paramref name="Paint"/> draws everything else, measured by the same call that built it.
/// <paramref name="Buttons"/> are the strip's clickable rects, panel-relative, for the overlay
/// to place its hit window over.</summary>
public sealed record GroupPanel(string Key, Rectangle Bounds, Rectangle FaceBox, Action<Graphics> Paint,
    Rectangle[]? Buttons = null);

/// <summary>Orbit, Poster, Glass dock, Monolith and Tiles: the set placed as a group at one position.</summary>
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
        WidgetTheme.Monolith => Monolith.Instance,
        WidgetTheme.Tiles => Tiles.Instance,
        _ => throw new NotSupportedException($"{theme} is not a group theme"),
    };

    /// <summary>The record is shown exactly when the per-widget overlay would show it.</summary>
    public static NowPlayingReading? TrackFor(DiscConfig disc, bool hasSource, NowPlayingReading reading) =>
        hasSource && !DiscRenderer.Measure(disc, reading).IsEmpty ? reading : null;

    public static BatteryReading? BatteryFor(HaloConfig halo, bool hasSource, BatteryReading reading) =>
        halo.Enabled && hasSource && reading.State != BatteryState.None ? reading : null;

    // ---- shared drawing ------------------------------------------------------------------------

    internal static int Pad(float scale) => (int)Math.Ceiling(ShadowRecipe.Default.Pad * Math.Max(scale, 0.05f));

    // ---- the music pill --------------------------------------------------------------------------

    /// <summary>The modern now-playing pill as one panel, measured once for size and painted with
    /// the exact same renderer as the Classic overlay. <paramref name="Buttons"/> ride on the panel
    /// so the overlay can position its invisible click window over them.</summary>
    public static MusicStrip.Metrics StripMetrics(NowPlayingReading track, string? source, float scale, float maxWidth = float.MaxValue)
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return MusicStrip.Measure(g, track, source, scale, maxWidth);
    }

    /// <summary>The strip plus the room its shadow needs on every side, with the buttons' rects
    /// already in panel coordinates. Every panel paint goes through TranslateTransform(pad, pad)
    /// or equivalent — the shadow is baked inside the panel bitmap, and clipping kills it.</summary>
    public readonly record struct StripLayout(int Pad, Size Size, MusicStrip.Metrics Metrics, Rectangle[] Buttons);

    public static StripLayout PaddedStrip(NowPlayingReading track, string? source, float scale, float maxWidth = float.MaxValue)
    {
        var metrics = StripMetrics(track, source, scale, maxWidth);
        int pad = Pad(scale);
        var buttons = new[]
        {
            Shift(metrics.PrevBox, pad, pad), Shift(metrics.PlayBox, pad, pad), Shift(metrics.NextBox, pad, pad),
        };
        var size = new Size(metrics.Size.Width + pad * 2, metrics.Size.Height + pad * 2);
        return new StripLayout(pad, size, metrics, buttons);

        static Rectangle Shift(Rectangle r, int dx, int dy) => new(r.X + dx, r.Y + dy, r.Width, r.Height);
    }

    public static GroupPanel StripPanel(string key, int x, int y, NowPlayingReading track,
        string? source, Bitmap? art, float scale)
    {
        var layout = PaddedStrip(track, source, scale);
        return new GroupPanel(key, new Rectangle(x, y, layout.Size.Width, layout.Size.Height),
            Rectangle.Empty,
            g =>
            {
                var inner = MusicStrip.Measure(g, track, source, scale);
                float phase = (Environment.TickCount64 % 100_000) / 100_000f;
                g.TranslateTransform(layout.Pad, layout.Pad);
                MusicStrip.Paint(g, inner, track, art, scale, phase);
            },
            layout.Buttons);
    }

    /// <summary>The time's drawn colour: the configured Clock.Color. The themes used to hard-code
    /// a white ink here, which made the Clock page's colour swatch a no-op in every theme but
    /// Classic.</summary>
    internal static Color ClockInk(ClockConfig clock) => ClockRenderer.ParseColor(clock.Color);

    /// <summary>The date's drawn colour: the configured DateColor, or the time's colour dimmed by
    /// DateOpacity — exactly Classic's rule.</summary>
    internal static Color DateInk(ClockConfig clock) => ClockRenderer.DateColorFor(clock, ClockInk(clock));

    internal static Color Dim(Color color, float opacity) =>
        Color.FromArgb((int)Math.Round(color.A * Math.Clamp(opacity, 0f, 1f)), color.R, color.G, color.B);

    /// <summary>A size from the mockup, in its units, at this scale.</summary>
    internal static float U(float mockupUnits, float scale) => mockupUnits * MockupUnit * scale;

    /// <summary>A text size from the mockup, lifted to 11 px at scale 1 where it would fall under.</summary>
    internal static float Text(float mockupUnits, float scale) => Math.Max(mockupUnits * MockupUnit, 11f) * scale;

    /// <summary>Monitor-relative top-left for a block at an anchor, inset by the margin. The margin gives
    /// way before the screen edge does: a block too big for its margins is pulled back on screen, and one
    /// bigger than the screen keeps its top left visible.</summary>
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
        x = Math.Max(work.Left, Math.Min(x, work.Right - size.Width));
        y = Math.Max(work.Top, Math.Min(y, work.Bottom - size.Height));
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
