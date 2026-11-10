using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using FeatherWall.Config;

namespace FeatherWall.Widgets.Themes;

/// <summary>The Editorial theme: one type family throughout, the date set as small accent capitals
/// above a light clock, a short accent rule under it, a real title hierarchy on the record and the
/// battery as a quiet ring.
///
/// Sizes are the 2026-11-10 mockup's proportions with the clock's 110 px as the unit, lifted where
/// they would fall under 11 px. Every surface carries the baked shadow's pad, and none of these
/// paint a shadow themselves: the overlay bakes one under the finished bitmap.</summary>
public static class Editorial
{
    private const float Unit = 110f;

    private const float TimePx = 110f, TimeTracking = -0.02f;

    // GDI+ reports a line box with the face's full ascent and descent. Digits use neither, so
    // stacked on that box the date and the rule float well clear of the time. TimeBox is the
    // trimmed box; the glyphs are drawn TrimTop above it.
    private const float TrimTop = 0.22f, TrimBottom = 0.18f;
    private const float DatePx = 15f, DateTracking = 0.32f, DateGap = 6f;
    private const float RuleWidth = 64f, RuleHeight = 2f, RuleGap = 12f;

    private const float DiscPx = 100f, RimStroke = 2.5f, RimGap = 5f, CaptionGap = 14f;
    private const float TitlePx = 19f, ArtistPx = 11f, ArtistTracking = 0.24f, ArtistGap = 6f;
    private const float PausedFade = 0.55f;

    private const float HaloPx = 52f, MinHalo = 12f;
    private const float InfoPx = 15f, InfoLineGap = 4f, InfoHaloGap = 10f;

    private static readonly Color Ink = Color.FromArgb(245, 255, 255, 255);
    private static readonly Color Low = Color.FromArgb(255, 0xFF, 0x4D, 0x4D);
    private static readonly Color Mid = Color.FromArgb(255, 0xFF, 0x9A, 0x3C);

    public readonly record struct ClockMetrics(
        string Time, string? Date, Rectangle DateBox, Rectangle TimeBox, RectangleF Rule, Size Total, float Scale);

    /// <summary><c>Clock.FontSize / 110</c> scales the whole theme, times the monitor's DPI factor.</summary>
    public static float Scale(ClockConfig clock, double dpi) =>
        Math.Max(clock.FontSize / Unit, 0.25f) * (float)Math.Max(dpi, 0.01);

    private static int Pad(float scale) => (int)Math.Ceiling(ShadowRecipe.Default.Pad * scale);
    private static int Ceil(float v) => (int)Math.Ceiling(v);

    // ---- clock ------------------------------------------------------------------------------

    public static string DateText(DateTime now) =>
        now.ToString("dddd · d MMMM", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);

    public static ClockMetrics MeasureClock(ClockConfig c, DateTime now, float scale)
    {
        scale = Math.Max(scale, 0.05f);
        string time = ClockLayout.TimeText(now, c.TwentyFourHour, c.ShowSeconds);
        string? date = c.ShowDate ? DateText(now) : null;

        using var timeFont = ThemeFonts.Create(ThemeFonts.DisplayLight, TimePx * scale);
        using var dateFont = ThemeFonts.Create(ThemeFonts.TextSemibold, DatePx * scale);
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        var ts = ThemeFonts.MeasureTracked(g, time, timeFont, TimePx * scale * TimeTracking);
        var ds = date is null ? SizeF.Empty : ThemeFonts.MeasureTracked(g, date, dateFont, DatePx * scale * DateTracking);

        int pad = Pad(scale);
        float ruleW = RuleWidth * scale, ruleH = Math.Max(RuleHeight * scale, 1f);
        int width = Ceil(Math.Max(Math.Max(ts.Width, ds.Width), ruleW)) + pad * 2;

        float y = pad;
        var dateBox = Rectangle.Empty;
        if (date is not null)
        {
            dateBox = new Rectangle((int)Math.Round((width - ds.Width) / 2f), (int)y, Ceil(ds.Width), Ceil(ds.Height));
            y = dateBox.Bottom + DateGap * scale;
        }
        float trimTop = TrimTop * TimePx * scale, trimBottom = TrimBottom * TimePx * scale;
        var timeBox = new Rectangle((int)Math.Round((width - ts.Width) / 2f), Ceil(y), Ceil(ts.Width),
                                    Math.Max(Ceil(ts.Height - trimTop - trimBottom), 1));
        var rule = new RectangleF((width - ruleW) / 2f, timeBox.Bottom + RuleGap * scale, ruleW, ruleH);
        int height = Ceil(rule.Bottom) + pad;

        return new ClockMetrics(time, date, dateBox, timeBox, rule, new Size(width, height), scale);
    }

    public static void PaintClock(Graphics g, in ClockMetrics m, Color accent)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        if (m.Date is not null)
        {
            using var dateFont = ThemeFonts.Create(ThemeFonts.TextSemibold, DatePx * m.Scale);
            using var dateBrush = new SolidBrush(accent);
            ThemeFonts.DrawTracked(g, m.Date, dateFont, dateBrush, m.DateBox.X, m.DateBox.Y, DatePx * m.Scale * DateTracking);
        }

        using (var timeFont = ThemeFonts.Create(ThemeFonts.DisplayLight, TimePx * m.Scale))
        using (var ink = new SolidBrush(Ink))
            ThemeFonts.DrawTracked(g, m.Time, timeFont, ink, m.TimeBox.X, m.TimeBox.Y - TrimTop * TimePx * m.Scale,
                                   TimePx * m.Scale * TimeTracking);

        using var rule = new SolidBrush(Color.FromArgb(230, accent));
        g.FillRectangle(rule, m.Rule);
    }

    // ---- record -----------------------------------------------------------------------------

    /// <summary>The same metrics type Classic uses, so the overlay's layer code takes either.
    /// <c>DiscBox</c> is the face alone; the progress rim sits outside it.</summary>
    public static NowPlayingRenderer.NowPlayingMetrics MeasureRecord(DiscConfig d, NowPlayingReading r, float scale)
    {
        scale = Math.Max(scale, 0.05f);
        var discOn = DiscRenderer.Measure(d, r);
        string title = r.Title ?? "";
        string artist = r.Artist?.ToUpper(CultureInfo.CurrentCulture) ?? "";
        if (discOn.IsEmpty && string.IsNullOrWhiteSpace(title))
            return new NowPlayingRenderer.NowPlayingMetrics(Size.Empty, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty);

        int pad = Pad(scale);
        int side = discOn.IsEmpty ? 0 : (int)Math.Round(DiscPx * scale);
        int ring = side == 0 || !d.ShowProgress ? 0 : Ceil((RimGap + RimStroke) * scale);
        int block = side + ring * 2;

        using var titleFont = ThemeFonts.Create(ThemeFonts.TextSemibold, TitlePx * scale);
        using var artistFont = ThemeFonts.Create(ThemeFonts.TextSemibold, ArtistPx * scale);
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        var titleSize = title.Length > 0 ? g.MeasureString(title, titleFont, PointF.Empty, ThemeFonts.Tight) : SizeF.Empty;
        var artistSize = artist.Length > 0
            ? ThemeFonts.MeasureTracked(g, artist, artistFont, ArtistPx * scale * ArtistTracking)
            : SizeF.Empty;

        int textW = Ceil(Math.Max(titleSize.Width, artistSize.Width));
        int textH = Ceil(titleSize.Height) + (artist.Length > 0 ? Ceil(ArtistGap * scale + artistSize.Height) : 0);
        int blockH = Math.Max(block, textH);

        var discBox = side == 0
            ? Rectangle.Empty
            : new Rectangle(pad + ring, pad + (blockH - block) / 2 + ring, side, side);

        int textX = pad + block + (block > 0 && textW > 0 ? Ceil(CaptionGap * scale) : 0);
        int textY = pad + (blockH - textH) / 2;
        var titleBox = title.Length > 0
            ? new Rectangle(textX, textY, Ceil(titleSize.Width), Ceil(titleSize.Height))
            : Rectangle.Empty;
        var artistBox = artist.Length > 0
            ? new Rectangle(textX, textY + Ceil(titleSize.Height) + Ceil(ArtistGap * scale),
                            Ceil(artistSize.Width), Ceil(artistSize.Height))
            : Rectangle.Empty;

        return new NowPlayingRenderer.NowPlayingMetrics(
            new Size(textX + textW + pad, blockH + pad * 2), discBox, titleBox, artistBox);
    }

    /// <summary>Everything about the record that does not turn: the rim and the caption. The face
    /// is the overlay's to draw, on its own layer or first into the same bitmap.</summary>
    public static void PaintRecordStatic(Graphics g, in NowPlayingRenderer.NowPlayingMetrics m, DiscConfig d,
                                         NowPlayingReading r, float progress, Color accent, float scale)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float fade = r.IsPlaying ? 1f : PausedFade;

        if (!m.DiscBox.IsEmpty && d.ShowProgress)
        {
            float stroke = Math.Max(RimStroke * scale, 1f);
            float out_ = (RimGap + RimStroke / 2f) * scale;
            var rim = RectangleF.Inflate(m.DiscBox, out_, out_);
            using (var track = new Pen(Color.FromArgb(r.IsPlaying ? 41 : 24, 255, 255, 255), stroke))
                g.DrawEllipse(track, rim);
            float sweep = 360f * Math.Clamp(progress, 0f, 1f);
            if (sweep > 0)
            {
                using var arc = new Pen(Color.FromArgb((int)(255 * fade), accent), stroke)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };
                g.DrawArc(arc, rim, -90f, sweep);
            }
        }

        if (!m.TitleBox.IsEmpty)
        {
            using var titleFont = ThemeFonts.Create(ThemeFonts.TextSemibold, TitlePx * scale);
            using var ink = new SolidBrush(Color.FromArgb((int)(Ink.A * fade), Ink));
            g.DrawString(r.Title, titleFont, ink, m.TitleBox.X, m.TitleBox.Y, ThemeFonts.Tight);
        }

        if (!m.ArtistBox.IsEmpty)
        {
            using var artistFont = ThemeFonts.Create(ThemeFonts.TextSemibold, ArtistPx * scale);
            using var brush = new SolidBrush(Color.FromArgb((int)(255 * fade), accent));
            ThemeFonts.DrawTracked(g, r.Artist!.ToUpper(CultureInfo.CurrentCulture), artistFont, brush,
                                   m.ArtistBox.X, m.ArtistBox.Y, ArtistPx * scale * ArtistTracking);
        }
    }

    // ---- battery ----------------------------------------------------------------------------

    /// <summary>Warnings keep their meaning: low and mid stay red and amber whatever the wallpaper
    /// is. Healthy and full take the accent.</summary>
    public static Color HaloColour(int percent, BatteryState state, HaloConfig h, Color accent)
    {
        if (state == BatteryState.Charged || !h.ColorByLevel) return accent;
        if (percent <= h.LowThreshold) return Low;
        if (percent <= h.MidThreshold) return Mid;
        return accent;
    }

    /// <summary>The ring alone, without the shadow's pad: the surface it lands in adds that.
    /// <c>Halo.Size</c> is Classic's; Editorial sizes the ring from the theme's scale.</summary>
    public static Size MeasureHalo(HaloConfig h, BatteryReading r, float scale)
    {
        if (!h.Enabled || r.State == BatteryState.None) return Size.Empty;
        int side = (int)Math.Round(Math.Max(HaloPx * Math.Max(scale, 0f), MinHalo));
        return new Size(side, side);
    }

    public static void PaintHalo(Graphics g, Rectangle box, HaloConfig h, BatteryReading r, Color accent)
    {
        if (box.Width <= 0 || box.Height <= 0 || r.State == BatteryState.None) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        float side = Math.Min(box.Width, box.Height);
        float u = side / HaloPx;
        float stroke = Math.Max(RimStroke * u, 1f);
        var ring = new RectangleF(box.X + stroke / 2f, box.Y + stroke / 2f, side - stroke, side - stroke);
        var colour = HaloColour(r.Percent, r.State, h, accent);

        using (var fill = new SolidBrush(Color.FromArgb(89, 0, 0, 0)))
            g.FillEllipse(fill, ring);
        using (var track = new Pen(Color.FromArgb(46, 255, 255, 255), stroke))
            g.DrawEllipse(track, ring);

        float sweep = r.State == BatteryState.Charged ? 360f : 360f * Math.Clamp(r.Percent, 0, 100) / 100f;
        if (sweep > 0)
        {
            using var arc = new Pen(colour, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(arc, ring, -90f, sweep);
        }

        float cx = box.X + side / 2f, cy = box.Y + side / 2f;
        using var ink = new SolidBrush(Ink);

        if (r.State == BatteryState.Charged)
        {
            using var tickFont = ThemeFonts.Create(ThemeFonts.TextSemibold, 17f * u);
            var t = g.MeasureString("✓", tickFont, PointF.Empty, ThemeFonts.Tight);
            g.DrawString("✓", tickFont, ink, cx - t.Width / 2f, cy - t.Height / 2f, ThemeFonts.Tight);
            return;
        }

        string number = r.Percent.ToString(CultureInfo.InvariantCulture);
        using var numberFont = ThemeFonts.Create(ThemeFonts.TextSemibold, 15f * u);
        using var percentFont = ThemeFonts.Create(ThemeFonts.TextSemibold, 8f * u);
        var ns = g.MeasureString(number, numberFont, PointF.Empty, ThemeFonts.Tight);
        var ps = g.MeasureString("%", percentFont, PointF.Empty, ThemeFonts.Tight);

        bool charging = r.State == BatteryState.Charging;
        float boltW = charging ? side * 0.10f : 0f, boltGap = charging ? side * 0.03f : 0f, kern = u;
        float left = cx - (boltW + boltGap + ns.Width + kern + ps.Width) / 2f;
        if (charging) BatteryHaloRenderer.PaintBolt(g, left + boltW / 2f, cy, side * 0.72f, colour);

        float x = left + boltW + boltGap;
        g.DrawString(number, numberFont, ink, x, cy - ns.Height / 2f, ThemeFonts.Tight);
        using var dim = new SolidBrush(Color.FromArgb(185, 255, 255, 255));
        g.DrawString("%", percentFont, dim, x + ns.Width + kern, cy + ns.Height / 2f - ps.Height, ThemeFonts.Tight);
    }

    // ---- info lines -------------------------------------------------------------------------

    /// <summary>The lines left over once the record and the ring have taken their sources, with
    /// the ring always on the left — <c>Halo.Placement</c> is Classic's.</summary>
    public static InfoRenderer.InfoMetrics MeasureInfo(InfoConfig i, IReadOnlyList<string?> values, float scale, Size halo)
    {
        scale = Math.Max(scale, 0.05f);
        float fontSize = InfoPx * scale;
        var lines = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();
        bool hasHalo = halo.Width > 0 && halo.Height > 0;
        if (lines.Count == 0 && !hasHalo)
            return new InfoRenderer.InfoMetrics(lines, Size.Empty, fontSize, Rectangle.Empty, Rectangle.Empty);

        var text = Size.Empty;
        if (lines.Count > 0)
        {
            using var font = ThemeFonts.Create(ThemeFonts.TextSemibold, fontSize);
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            float w = 0, hgt = 0;
            foreach (var line in lines)
            {
                var s = g.MeasureString(line, font, PointF.Empty, ThemeFonts.Tight);
                w = Math.Max(w, s.Width);
                hgt += s.Height + InfoLineGap * scale;
            }
            text = new Size(Ceil(w), Ceil(hgt - InfoLineGap * scale));
        }

        int pad = Pad(scale);
        int gap = hasHalo && lines.Count > 0 ? Ceil(InfoHaloGap * scale) : 0;
        int blockH = Math.Max(text.Height, halo.Height);
        var haloBox = hasHalo ? new Rectangle(pad, pad + (blockH - halo.Height) / 2, halo.Width, halo.Height) : Rectangle.Empty;
        var textBox = new Rectangle(pad + halo.Width + gap, pad + (blockH - text.Height) / 2, text.Width, text.Height);
        var total = new Size(halo.Width + gap + text.Width + pad * 2, blockH + pad * 2);
        return new InfoRenderer.InfoMetrics(lines, total, fontSize, textBox, haloBox);
    }

    public static void PaintInfo(Graphics g, in InfoRenderer.InfoMetrics m)
    {
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float scale = m.FontSize / InfoPx;
        using var font = ThemeFonts.Create(ThemeFonts.TextSemibold, m.FontSize);
        using var brush = new SolidBrush(Color.FromArgb(225, 255, 255, 255));
        float y = m.TextBox.Y;
        foreach (var line in m.Lines)
        {
            g.DrawString(line, font, brush, m.TextBox.X, y, ThemeFonts.Tight);
            y += g.MeasureString(line, font, PointF.Empty, ThemeFonts.Tight).Height + InfoLineGap * scale;
        }
    }
}
