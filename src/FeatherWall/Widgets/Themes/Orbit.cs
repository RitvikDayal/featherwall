using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using FeatherWall.Config;

namespace FeatherWall.Widgets.Themes;

/// <summary>Orbit: everything becomes a ring. The clock is a dial whose accent arc fills as the
/// hour passes, with the record and the battery as smaller moons beneath it, each captioned. One
/// panel; the hour arc moves at the clock's existing repaint, so it adds no timer.
///
/// Proportions are the 2026-11-10 mockup's, through <see cref="GroupThemes.MockupUnit"/>.</summary>
public sealed class Orbit : IGroupTheme
{
    public static readonly Orbit Instance = new();

    public ClockAnchor DefaultPosition => ClockAnchor.TopLeft;

    private const float Dial = 340f, Satellite = 128f, RowGap = 34f, SatGap = 34f, CaptionGap = 10f;
    private const float TimePx = 96f, DatePx = 19f, CaptionPx = 15f;

    private static readonly Color Disc = Color.FromArgb(97, 10, 8, 16);
    private static readonly Color Ink = Color.FromArgb(245, 255, 255, 255);

    /// <summary>How far through the hour, 0 to 1 — the dial's accent arc.</summary>
    public static float HourFraction(DateTime now) => (now.Minute * 60 + now.Second) / 3600f;

    public IReadOnlyList<GroupPanel> Layout(GroupInput input)
    {
        float s = Math.Max(input.Scale, 0.05f);
        bool clock = input.Clock.Enabled;
        var track = input.Track;
        var battery = input.Battery;
        int moons = (track is null ? 0 : 1) + (battery is null ? 0 : 1);
        if (!clock && moons == 0) return [];

        float dial = GroupThemes.U(Dial, s), sat = GroupThemes.U(Satellite, s);
        float rowGap = GroupThemes.U(RowGap, s), satGap = GroupThemes.U(SatGap, s);
        float captionGap = GroupThemes.U(CaptionGap, s), captionPx = GroupThemes.Text(CaptionPx, s);
        float captionWidth = sat + satGap * 0.8f;
        float tracking = captionPx * 0.14f;

        using var captionFont = ThemeFonts.Create(ThemeFonts.Bahn, captionPx);
        float lineH;
        using (var mg = Graphics.FromHwnd(IntPtr.Zero))
            lineH = mg.MeasureString("Ag", captionFont, PointF.Empty, ThemeFonts.Tight).Height;
        int captionLines = track is not null ? 2 : moons > 0 ? 1 : 0;

        float rowWidth = moons == 0 ? 0 : moons * captionWidth + (moons - 1) * (satGap * 0.2f);
        float colWidth = Math.Max(clock ? dial : 0, rowWidth);
        float rowHeight = moons == 0 ? 0 : sat + captionGap + captionLines * lineH;
        float colHeight = (clock ? dial : 0) + (clock && moons > 0 ? rowGap : 0) + rowHeight;

        int pad = GroupThemes.Pad(s);
        var size = new Size((int)Math.Ceiling(colWidth) + pad * 2, (int)Math.Ceiling(colHeight) + pad * 2);
        var origin = GroupThemes.Place(input.WorkArea, size, input.Position ?? DefaultPosition, GroupThemes.Margin(input));

        // Where everything sits inside the panel.
        var dialBox = new RectangleF(pad + (colWidth - dial) / 2f, pad, dial, dial);
        float rowTop = pad + (clock ? dial + (moons > 0 ? rowGap : 0) : 0);
        float rowLeft = pad + (colWidth - rowWidth) / 2f;
        var slots = new List<(RectangleF Ring, RectangleF Caption, bool IsRecord)>();
        for (int i = 0, k = 0; i < 2; i++)
        {
            bool isRecord = i == 0;
            if (isRecord ? track is null : battery is null) continue;
            float x = rowLeft + k * (captionWidth + satGap * 0.2f);
            var ring = new RectangleF(x + (captionWidth - sat) / 2f, rowTop, sat, sat);
            var caption = new RectangleF(x, rowTop + sat + captionGap, captionWidth, captionLines * lineH);
            slots.Add((ring, caption, isRecord));
            k++;
        }

        var faceBox = Rectangle.Empty;
        var recordSlot = slots.FirstOrDefault(sl => sl.IsRecord);
        if (track is not null)
        {
            float inset = recordSlot.Ring.Width * 12f / 128f;
            var face = RectangleF.Inflate(recordSlot.Ring, -inset, -inset);
            faceBox = Rectangle.Round(face);
        }

        void Paint(Graphics g)
        {
            // Fonts are made again here: the ones Layout measured with are disposed by the time a
            // panel is painted.
            using var captionFont = ThemeFonts.Create(ThemeFonts.Bahn, captionPx);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            if (clock) PaintDial(g, dialBox, input, s);
            foreach (var (ring, caption, isRecord) in slots)
            {
                if (isRecord) PaintRecordRing(g, ring, input, s, caption, captionFont, tracking, lineH);
                else PaintBatteryRing(g, ring, input, s, caption, captionFont, tracking);
            }
        }

        return [new GroupPanel("orbit", new Rectangle(origin, size), faceBox, Paint)];
    }

    private static void PaintDial(Graphics g, RectangleF box, GroupInput input, float s)
    {
        float k = box.Width / Dial;
        var ring = RectangleF.Inflate(box, -10f * k, -10f * k);
        float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f;
        float sweep = 360f * HourFraction(input.Now);

        GroupThemes.Ring(g, ring, Math.Max(3f * k, 1f), Disc, Color.FromArgb(36, 255, 255, 255),
                         input.Accent, sweep, Math.Max(5f * k, 1.5f));

        using (var tick = new Pen(Color.FromArgb(102, 255, 255, 255), Math.Max(2f * k, 1f)))
            for (int q = 0; q < 4; q++)
            {
                double a = q * Math.PI / 2;
                float r1 = 152f * k, r2 = 140f * k;
                g.DrawLine(tick, cx + (float)Math.Sin(a) * r1, cy - (float)Math.Cos(a) * r1,
                                 cx + (float)Math.Sin(a) * r2, cy - (float)Math.Cos(a) * r2);
            }

        if (sweep > 0)
        {
            double end = (sweep - 90f) * Math.PI / 180.0;
            float r = ring.Width / 2f, dot = 7f * k;
            using var brush = new SolidBrush(input.Accent);
            g.FillEllipse(brush, cx + (float)Math.Cos(end) * r - dot, cy + (float)Math.Sin(end) * r - dot, dot * 2, dot * 2);
        }

        string time = ClockLayout.TimeText(input.Now, input.Clock.TwentyFourHour, input.Clock.ShowSeconds);
        string? date = input.Clock.ShowDate
            ? input.Now.ToString("ddd d MMM", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture)
            : null;
        // Seconds widen the time; shrink it to stay inside the ring rather than spill over it.
        float timePx = GroupThemes.U(TimePx, s);
        using var timeFont = ThemeFonts.Create(ThemeFonts.BahnLight, timePx);
        var ts = g.MeasureString(time, timeFont, PointF.Empty, ThemeFonts.Tight);
        float maxTime = ring.Width * 0.78f;
        using var fitted = ts.Width > maxTime ? ThemeFonts.Create(ThemeFonts.BahnLight, timePx * maxTime / ts.Width) : null;
        var useTime = fitted ?? timeFont;
        ts = g.MeasureString(time, useTime, PointF.Empty, ThemeFonts.Tight);

        float datePx = GroupThemes.Text(DatePx, s), dateTracking = datePx * 0.2f, gap = GroupThemes.U(12f, s);
        using var dateFont = ThemeFonts.Create(ThemeFonts.Bahn, datePx);
        var ds = date is null ? SizeF.Empty : ThemeFonts.MeasureTracked(g, date, dateFont, dateTracking);
        float stack = ts.Height + (date is null ? 0 : gap + ds.Height);
        float y = cy - stack / 2f;

        using (var ink = new SolidBrush(Ink))
            g.DrawString(time, useTime, ink, cx - ts.Width / 2f, y, ThemeFonts.Tight);
        if (date is null) return;
        using var dim = new SolidBrush(Color.FromArgb(191, 255, 255, 255));
        ThemeFonts.DrawTracked(g, date, dateFont, dim, cx - ds.Width / 2f, y + ts.Height + gap, dateTracking);
    }

    private static void PaintRecordRing(Graphics g, RectangleF box, GroupInput input, float s,
                                        RectangleF caption, Font font, float tracking, float lineH)
    {
        var r = input.Track!.Value;
        float k = box.Width / Satellite;
        var ring = RectangleF.Inflate(box, -4f * k, -4f * k);
        float fade = r.IsPlaying ? 1f : 0.55f;
        float sweep = input.Disc.ShowProgress ? 360f * (float)Math.Clamp(r.Progress, 0, 1) : 0f;
        GroupThemes.Ring(g, ring, Math.Max(3f * k, 1f), Disc, Color.FromArgb(41, 255, 255, 255),
                         GroupThemes.Alpha(input.Accent, fade), sweep);

        string title = (r.Title ?? "").ToUpper(CultureInfo.CurrentCulture);
        string artist = (r.Artist ?? "").ToUpper(CultureInfo.CurrentCulture);
        CaptionLine(g, title, font, Color.FromArgb((int)(230 * fade), 255, 255, 255), caption, 0, tracking);
        CaptionLine(g, artist, font, Color.FromArgb((int)(138 * fade), 255, 255, 255), caption, lineH, tracking);
    }

    private static void PaintBatteryRing(Graphics g, RectangleF box, GroupInput input, float s,
                                         RectangleF caption, Font font, float tracking)
    {
        var r = input.Battery!.Value;
        float k = box.Width / Satellite;
        var ring = RectangleF.Inflate(box, -4f * k, -4f * k);
        var colour = Editorial.HaloColour(r.Percent, r.State, input.Halo, input.Accent);
        float sweep = r.State == BatteryState.Charged ? 360f : 360f * Math.Clamp(r.Percent, 0, 100) / 100f;
        GroupThemes.Ring(g, ring, Math.Max(3f * k, 1f), Disc, Color.FromArgb(41, 255, 255, 255), colour, sweep);

        var centre = new PointF(box.X + box.Width / 2f, box.Y + box.Height / 2f);
        GroupThemes.BatteryCentre(g, centre, box.Width, r, colour, ThemeFonts.Bahn,
                                  GroupThemes.U(30f, s), GroupThemes.U(16f, s));

        string word = r.State switch
        {
            BatteryState.Charging => "CHARGING",
            BatteryState.Charged => "CHARGED",
            _ => "BATTERY",
        };
        CaptionLine(g, word, font, Color.FromArgb(184, 255, 255, 255), caption, 0, tracking);
    }

    private static void CaptionLine(Graphics g, string text, Font font, Color colour, RectangleF box, float dy, float tracking)
    {
        if (text.Length == 0) return;
        text = GroupThemes.FitText(g, text, font, box.Width, tracking);
        var size = ThemeFonts.MeasureTracked(g, text, font, tracking);
        using var brush = new SolidBrush(colour);
        ThemeFonts.DrawTracked(g, text, font, brush, box.X + (box.Width - size.Width) / 2f, box.Y + dy, tracking);
    }
}
