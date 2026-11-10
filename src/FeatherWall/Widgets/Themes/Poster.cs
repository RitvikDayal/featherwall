using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using FeatherWall.Config;

namespace FeatherWall.Widgets.Themes;

/// <summary>Poster: typography as the wallpaper's frame. A huge stacked clock — outlined hours,
/// accent-filled minutes — with the date running down beside it. The record shrinks to a token
/// beside one line of text, and the battery becomes a slim bar.
///
/// Placement follows the approved mockup: the record line sits at the bottom corner horizontally
/// opposite the clock, the battery at the bottom corner on the clock's side, both mirrored to the
/// top when the clock is at the bottom. A centre anchor counts as right and top.</summary>
public sealed class Poster : IGroupTheme
{
    public static readonly Poster Instance = new();

    public ClockAnchor DefaultPosition => ClockAnchor.TopRight;

    private const float TimePx = 250f, LineHeight = 0.86f, DatePx = 19f, DateGap = 33f;
    private const float Token = 64f, TokenGap = 18f, LinePx = 22f, CellWidth = 110f, CellHeight = 10f, CellGap = 12f;

    public IReadOnlyList<GroupPanel> Layout(GroupInput input)
    {
        float s = Math.Max(input.Scale, 0.05f);
        var anchor = input.Position ?? DefaultPosition;
        int col = (int)anchor % 3, row = (int)anchor / 3;
        bool clockRight = col != 0, clockTop = row != 2;
        int margin = GroupThemes.Margin(input);
        var panels = new List<GroupPanel>();

        if (input.Clock.Enabled) panels.Add(ClockPanel(input, s, anchor, margin));

        var near = clockTop ? (clockRight ? ClockAnchor.BottomRight : ClockAnchor.BottomLeft)
                            : (clockRight ? ClockAnchor.TopRight : ClockAnchor.TopLeft);
        var far = clockTop ? (clockRight ? ClockAnchor.BottomLeft : ClockAnchor.BottomRight)
                           : (clockRight ? ClockAnchor.TopLeft : ClockAnchor.TopRight);
        if (input.Track is not null) panels.Add(RecordPanel(input, s, far, margin));
        if (input.Battery is not null) panels.Add(BatteryPanel(input, s, near, margin));
        return panels;
    }

    // ---- clock -------------------------------------------------------------------------------

    private static GroupPanel ClockPanel(GroupInput input, float s, ClockAnchor anchor, int margin)
    {
        var now = input.Now;
        string hours = now.ToString(input.Clock.TwentyFourHour ? "HH" : "%h", CultureInfo.InvariantCulture);
        string minutes = now.ToString("mm", CultureInfo.InvariantCulture);
        string? seconds = input.Clock.ShowSeconds ? now.ToString("ss", CultureInfo.InvariantCulture) : null;
        string? date = input.Clock.ShowDate
            ? now.ToString("dddd d MMMM", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture)
            : null;

        float em = GroupThemes.U(TimePx, s);
        float lineH = em * LineHeight;
        // Measured here, built again in Paint: a path is transformed in place when drawn, so one
        // shared between the two would be moved twice by a second paint.
        RectangleF hb, mb, sb = RectangleF.Empty;
        using (var family = ResolveFamily())
        using (var hp = GlyphPath(family, hours, em))
        using (var mp = GlyphPath(family, minutes, em))
        {
            hb = hp.GetBounds();
            mb = mp.GetBounds();
            if (seconds is not null)
                using (var sp = GlyphPath(family, seconds, em * 0.4f)) sb = sp.GetBounds();
        }
        float stroke = Math.Max(GroupThemes.U(3f, s), 1f);
        float blockW = Math.Max(Math.Max(hb.Right, mb.Right), sb.Right) + stroke;
        float blockH = lineH + mb.Bottom + (seconds is null ? 0 : lineH * 0.45f + sb.Height);

        float datePx = GroupThemes.Text(DatePx, s), tracking = datePx * 0.36f;
        using var dateFont = ThemeFonts.Create(ThemeFonts.BahnSemibold, datePx);
        SizeF ds = SizeF.Empty;
        if (date is not null)
            using (var mg = Graphics.FromHwnd(IntPtr.Zero))
                ds = ThemeFonts.MeasureTracked(mg, date, dateFont, tracking);
        float dateGap = date is null ? 0 : GroupThemes.U(DateGap, s);
        // The date runs down beside the time, so its length is a height and its line a width.
        float width = blockW + dateGap + ds.Height;
        float height = Math.Max(blockH, date is null ? 0 : GroupThemes.U(18f, s) + ds.Width);

        int pad = GroupThemes.Pad(s);
        var size = new Size((int)Math.Ceiling(width) + pad * 2, (int)Math.Ceiling(height) + pad * 2);
        var origin = GroupThemes.Place(input.WorkArea, size, anchor, margin);

        void Paint(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            // Fonts are made again here: the ones Layout measured with are disposed by the time a
            // panel is painted.
            using var dateFont = ThemeFonts.Create(ThemeFonts.BahnSemibold, datePx);
            using var family = ResolveFamily();
            using var hourPath = GlyphPath(family, hours, em);
            using var minutePath = GlyphPath(family, minutes, em);
            using var secondPath = seconds is null ? null : GlyphPath(family, seconds, em * 0.4f);
            var state = g.Save();
            // Right-aligned stack, as the mockup sets it.
            g.TranslateTransform(pad, pad);
            using (var outline = new Pen(Color.FromArgb(235, 255, 255, 255), stroke) { LineJoin = LineJoin.Round })
            {
                var m = new Matrix();
                m.Translate(blockW - hb.Right - stroke / 2f, -hb.Top + stroke / 2f);
                hourPath.Transform(m);
                g.DrawPath(outline, hourPath);
            }
            using (var fill = new SolidBrush(input.Accent))
            {
                var m = new Matrix();
                m.Translate(blockW - mb.Right - stroke / 2f, lineH - hb.Top + stroke / 2f);
                minutePath.Transform(m);
                g.FillPath(fill, minutePath);
            }
            if (secondPath is not null)
            {
                using var dim = new SolidBrush(Color.FromArgb(153, 255, 255, 255));
                var m = new Matrix();
                m.Translate(blockW - sb.Right - stroke / 2f, lineH + mb.Bottom - hb.Top + lineH * 0.45f - sb.Top);
                secondPath.Transform(m);
                g.FillPath(dim, secondPath);
            }
            if (date is not null)
            {
                // Clockwise quarter turn: the date reads top to bottom, like writing-mode vertical-rl.
                g.TranslateTransform(blockW + dateGap + ds.Height, GroupThemes.U(18f, s));
                g.RotateTransform(90f);
                using var brush = new SolidBrush(Color.FromArgb(217, 255, 255, 255));
                ThemeFonts.DrawTracked(g, date, dateFont, brush, 0, 0, tracking);
            }
            g.Restore(state);
        }

        return new GroupPanel("poster-clock", new Rectangle(origin, size), Rectangle.Empty, Paint);
    }

    private static FontFamily ResolveFamily()
    {
        foreach (var name in ThemeFonts.Bahn)
            if (ThemeFonts.IsInstalled(name)) return new FontFamily(name);
        return new FontFamily("Segoe UI");
    }

    private static GraphicsPath GlyphPath(FontFamily family, string text, float em)
    {
        var path = new GraphicsPath();
        path.AddString(text, family, (int)FontStyle.Bold, em, PointF.Empty, StringFormat.GenericTypographic);
        return path;
    }

    // ---- record line --------------------------------------------------------------------------

    private static GroupPanel RecordPanel(GroupInput input, float s, ClockAnchor corner, int margin)
    {
        var r = input.Track!.Value;
        float token = GroupThemes.U(Token, s), gap = GroupThemes.U(TokenGap, s);
        float px = GroupThemes.Text(LinePx, s), tracking = px * 0.1f;
        // Capped well short of the screen: a 93-character YouTube title ran as a thin line across half of it.
        float maxLine = Math.Min(input.WorkArea.Width * 0.42f, GroupThemes.U(620f, s)) - token - gap;
        using var titleFont = ThemeFonts.Create(ThemeFonts.BahnSemibold, px);
        using var artistFont = ThemeFonts.Create(ThemeFonts.Bahn, px);

        string title = (r.Title ?? "").ToUpper(CultureInfo.CurrentCulture);
        string artist = string.IsNullOrWhiteSpace(r.Artist) ? "" : " — " + r.Artist!.ToUpper(CultureInfo.CurrentCulture);
        SizeF ts, asz;
        using (var mg = Graphics.FromHwnd(IntPtr.Zero))
        {
            title = GroupThemes.FitText(mg, title, titleFont, maxLine, tracking);
            ts = ThemeFonts.MeasureTracked(mg, title, titleFont, tracking);
            artist = artist.Length == 0 ? "" : GroupThemes.FitText(mg, artist, artistFont, Math.Max(maxLine - ts.Width, 0), tracking);
            asz = artist.Length == 0 ? SizeF.Empty : ThemeFonts.MeasureTracked(mg, artist, artistFont, tracking);
        }

        int pad = GroupThemes.Pad(s);
        float textW = ts.Width + asz.Width, textH = Math.Max(ts.Height, asz.Height);
        float blockH = Math.Max(token, textH);
        var size = new Size((int)Math.Ceiling(token + gap + textW) + pad * 2, (int)Math.Ceiling(blockH) + pad * 2);
        var origin = GroupThemes.Place(input.WorkArea, size, corner, margin);
        var face = Rectangle.Round(new RectangleF(pad, pad + (blockH - token) / 2f, token, token));
        float fade = r.IsPlaying ? 1f : 0.55f;

        void Paint(Graphics g)
        {
            // Fonts are made again here: the ones Layout measured with are disposed by the time a
            // panel is painted.
            using var titleFont = ThemeFonts.Create(ThemeFonts.BahnSemibold, px);
            using var artistFont = ThemeFonts.Create(ThemeFonts.Bahn, px);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float x = pad + token + gap, y = pad + (blockH - textH) / 2f;
            using (var ink = new SolidBrush(Color.FromArgb((int)(245 * fade), 255, 255, 255)))
                ThemeFonts.DrawTracked(g, title, titleFont, ink, x, y, tracking);
            if (artist.Length == 0) return;
            using var dim = new SolidBrush(Color.FromArgb((int)(158 * fade), 255, 255, 255));
            ThemeFonts.DrawTracked(g, artist, artistFont, dim, x + ts.Width, y, tracking);
        }

        return new GroupPanel("poster-record", new Rectangle(origin, size), face, Paint);
    }

    // ---- battery bar --------------------------------------------------------------------------

    private static GroupPanel BatteryPanel(GroupInput input, float s, ClockAnchor corner, int margin)
    {
        var r = input.Battery!.Value;
        float px = GroupThemes.Text(LinePx, s), tracking = px * 0.1f;
        float cellW = GroupThemes.U(CellWidth, s), cellH = Math.Max(GroupThemes.U(CellHeight, s), 3f);
        float gap = GroupThemes.U(CellGap, s);
        bool charging = r.State == BatteryState.Charging;
        string text = r.State == BatteryState.Charged ? "FULL" : $"{r.Percent}%";
        using var font = ThemeFonts.Create(ThemeFonts.BahnSemibold, px);
        SizeF ts;
        using (var mg = Graphics.FromHwnd(IntPtr.Zero))
            ts = ThemeFonts.MeasureTracked(mg, text, font, tracking);
        float bolt = charging ? px * 0.7f : 0f;

        int pad = GroupThemes.Pad(s);
        float w = bolt + ts.Width + gap + cellW, h = Math.Max(ts.Height, cellH);
        var size = new Size((int)Math.Ceiling(w) + pad * 2, (int)Math.Ceiling(h) + pad * 2);
        var origin = GroupThemes.Place(input.WorkArea, size, corner, margin);
        var colour = Editorial.HaloColour(r.Percent, r.State, input.Halo, input.Accent);

        void Paint(Graphics g)
        {
            // Fonts are made again here: the ones Layout measured with are disposed by the time a
            // panel is painted.
            using var font = ThemeFonts.Create(ThemeFonts.BahnSemibold, px);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float cy = pad + h / 2f;
            if (charging) BatteryHaloRenderer.PaintBolt(g, pad + bolt / 2f, cy, px * 1.6f, colour);
            using (var ink = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
                ThemeFonts.DrawTracked(g, text, font, ink, pad + bolt, cy - ts.Height / 2f, tracking);

            var cell = new RectangleF(pad + bolt + ts.Width + gap, cy - cellH / 2f, cellW, cellH);
            using (var track = new SolidBrush(Color.FromArgb(46, 255, 255, 255)))
                FillRounded(g, track, cell, cellH / 2f);
            float filled = r.State == BatteryState.Charged ? 1f : Math.Clamp(r.Percent, 0, 100) / 100f;
            if (filled <= 0) return;
            using var level = new SolidBrush(colour);
            FillRounded(g, level, cell with { Width = Math.Max(cell.Width * filled, cellH) }, cellH / 2f);
        }

        return new GroupPanel("poster-battery", new Rectangle(origin, size), Rectangle.Empty, Paint);
    }

    private static void FillRounded(Graphics g, Brush brush, RectangleF r, float radius)
    {
        using var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 90, 180);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
