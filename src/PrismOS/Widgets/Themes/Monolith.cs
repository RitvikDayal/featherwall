using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using PrismOS.Config;

namespace PrismOS.Widgets.Themes;

/// <summary>Monolith: the weekday is the widget. Thin condensed capitals spaced far apart, the date
/// and the time as small tracked lines under it, and the music and battery as one stacked set at the
/// opposite edge. Two panels: the day block at the chosen position, the music set at the other end
/// of the same column, so the pair frames the wallpaper rather than crowding one corner.
///
/// Text aligns to the column it sits in: left, centred or right. Proportions are the 2026-11-10
/// prototype's, through <see cref="GroupThemes.MockupUnit"/>.</summary>
public sealed class Monolith : IGroupTheme
{
    public static readonly Monolith Instance = new();

    public ClockAnchor DefaultPosition => ClockAnchor.TopLeft;

    private const float DayPx = 118f, DayTracking = 0.62f;
    private const float LinePx = 30f, LineTracking = 0.3f, DateGap = 36f, TimeGap = 22f;
    private const float StripPx = 22f, StripTracking = 0.32f, StripGap = 22f, MaxText = 640f;

    private static readonly Color Ink = Color.FromArgb(245, 255, 255, 255);
    private static readonly Color Dim = Color.FromArgb(140, 255, 255, 255);

    public IReadOnlyList<GroupPanel> Layout(GroupInput input)
    {
        float s = Math.Max(input.Scale, 0.05f);
        var anchor = input.Position ?? DefaultPosition;
        int col = (int)anchor % 3, row = (int)anchor / 3;
        int margin = GroupThemes.Margin(input);
        int pad = GroupThemes.Pad(s);
        var panels = new List<GroupPanel>();

        using var mg = Graphics.FromHwnd(IntPtr.Zero);

        if (input.Clock.Enabled)
        {
            var lines = DayLines(input, s);
            var day = Measure(mg, lines, s);
            // A long weekday ("QUARTA-FEIRA") at a large size outgrows the screen; shrink the day, not the
            // smaller lines, until the block fits between the margins.
            float room = input.WorkArea.Width - margin * 2 - pad * 2;
            if (day.Width > room)
            {
                lines = lines with { DayPx = lines.DayPx * room / day.Width * 0.98f };
                day = Measure(mg, lines, s);
            }
            var size = new Size((int)Math.Ceiling(day.Width) + pad * 2, (int)Math.Ceiling(day.Height) + pad * 2);
            var origin = GroupThemes.Place(input.WorkArea, size, anchor, margin);
            panels.Add(new GroupPanel("monolith-day", new Rectangle(origin, size), Rectangle.Empty,
                g => PaintDay(g, lines, input.Clock, s, pad, size.Width - pad * 2, col)));
        }

        var track = input.Track;
        var battery = input.Battery;
        if (track is not null || battery is not null)
        {
            // The pill and, beneath it, the battery as a quiet tracked line: one stacked set,
            // pinned to the opposite end of the same column.
            // Cap short of the screen: at very large sizes the weekday can move, the pill cannot be allowed to run off the edge.
            float stripCap = Math.Min(input.WorkArea.Width * 0.42f, GroupThemes.U(MaxText, s) + 400f * s);
            var strip = track is null ? (GroupThemes.StripLayout?)null : GroupThemes.PaddedStrip(track.Value, input.Source, s, stripCap);
            var set = PowerSet(mg, battery, s, strip?.Size.Width ?? 0f, strip?.Size.Height ?? 0f, track);
            var size = new Size((int)Math.Ceiling(set.Width) + pad * 2, (int)Math.Ceiling(set.Height) + pad * 2);
            // The opposite end of the same column, or the anchor itself when there is no day block.
            var stripAnchor = panels.Count == 0 ? anchor : (ClockAnchor)((row == 2 ? 0 : 2) * 3 + col);
            var origin = GroupThemes.Place(input.WorkArea, size, stripAnchor, margin);

            Rectangle[]? buttons = null;
            IReadOnlyList<PanelBlur.Region>? cardRegions = null;
            if (strip is { } sl && set.StripOrigin is { } so)
            {
                buttons =
                [
                    new Rectangle(sl.Buttons[0].X + so.X + pad, sl.Buttons[0].Y + so.Y + pad, sl.Buttons[0].Width, sl.Buttons[0].Height),
                    new Rectangle(sl.Buttons[1].X + so.X + pad, sl.Buttons[1].Y + so.Y + pad, sl.Buttons[1].Width, sl.Buttons[1].Height),
                    new Rectangle(sl.Buttons[2].X + so.X + pad, sl.Buttons[2].Y + so.Y + pad, sl.Buttons[2].Width, sl.Buttons[2].Height),
                ];
                cardRegions = [new PanelBlur.Region(new RectangleF(pad + so.X + sl.Pad, pad + so.Y + sl.Pad, sl.Metrics.Size.Width, sl.Metrics.Size.Height), sl.Metrics.Size.Height * 0.3f)];
            }

            panels.Add(new GroupPanel("monolith-strip", new Rectangle(origin, size), Rectangle.Empty,
                g => PaintMusicSet(g, set, track, battery, input, s, pad), buttons, cardRegions));
        }
        return panels;
    }

    // ---- the day block -----------------------------------------------------------------------

    private sealed record DayText(string Day, string? Date, string Time, float DayPx);

    private static DayText DayLines(GroupInput input, float s)
    {
        var culture = CultureInfo.CurrentCulture;
        string day = input.Now.ToString("dddd", culture).ToUpper(culture);
        string? date = input.Clock.ShowDate ? (input.Now.ToString("d MMMM, yyyy", culture) + ".").ToUpper(culture) : null;
        string time = "— " + ClockLayout.TimeText(input.Now, input.Clock.TwentyFourHour, input.Clock.ShowSeconds) + " —";
        return new DayText(day, date, time, GroupThemes.U(DayPx, s));
    }

    private static SizeF Measure(Graphics g, DayText t, float s)
    {
        using var dayFont = ThemeFonts.Create(ThemeFonts.BahnCondensedLight, t.DayPx);
        using var dateFont = ThemeFonts.Create(ThemeFonts.BahnSemibold, GroupThemes.Text(LinePx, s));
        using var timeFont = ThemeFonts.Create(ThemeFonts.Bahn, GroupThemes.Text(LinePx, s));
        var day = ThemeFonts.MeasureTracked(g, t.Day, dayFont, dayFont.Size * DayTracking);
        var date = t.Date is null ? SizeF.Empty : ThemeFonts.MeasureTracked(g, t.Date, dateFont, dateFont.Size * LineTracking);
        var time = ThemeFonts.MeasureTracked(g, t.Time, timeFont, timeFont.Size * LineTracking);
        float height = day.Height + (t.Date is null ? 0 : GroupThemes.U(DateGap, s) + date.Height)
                     + GroupThemes.U(t.Date is null ? DateGap : TimeGap, s) + time.Height;
        return new SizeF(Math.Max(day.Width, Math.Max(date.Width, time.Width)), height);
    }

    private static void PaintDay(Graphics g, DayText t, ClockConfig clock, float s, int pad, float width, int col)
    {
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var dayFont = ThemeFonts.Create(ThemeFonts.BahnCondensedLight, t.DayPx);
        using var dateFont = ThemeFonts.Create(ThemeFonts.BahnSemibold, GroupThemes.Text(LinePx, s));
        using var timeFont = ThemeFonts.Create(ThemeFonts.Bahn, GroupThemes.Text(LinePx, s));
        using var ink = new SolidBrush(GroupThemes.ClockInk(clock));
        using var dim = new SolidBrush(GroupThemes.DateInk(clock));

        float y = pad;
        float Line(string text, Font font, Brush brush, float tracking)
        {
            var size = ThemeFonts.MeasureTracked(g, text, font, tracking);
            float x = pad + col switch { 0 => 0f, 1 => (width - size.Width) / 2f, _ => width - size.Width };
            ThemeFonts.DrawTracked(g, text, font, brush, x, y, tracking);
            y += size.Height;
            return size.Height;
        }

        y += Line(t.Day, dayFont, ink, dayFont.Size * DayTracking);
        if (t.Date is not null)
        {
            y += GroupThemes.U(DateGap, s);
            y += Line(t.Date, dateFont, dim, dateFont.Size * LineTracking);
            y += GroupThemes.U(TimeGap, s);
        }
        else
        {
            y += GroupThemes.U(DateGap, s);
        }
        Line(t.Time, timeFont, ink, timeFont.Size * LineTracking);
    }

    // ---- the music set -----------------------------------------------------------------------

    private readonly record struct SetLayout(float Width, float Height, Point? StripOrigin,
        float PowerTop, string? Power, bool Charging, bool Low);

    private static SetLayout PowerSet(Graphics mg, BatteryReading? battery, float s,
        float stripW, float stripH, NowPlayingReading? track)
    {
        float px = GroupThemes.Text(StripPx, s), tracking = px * StripTracking, gap = GroupThemes.U(StripGap, s);
        using var font = ThemeFonts.Create(ThemeFonts.Bahn, px);
        float lineH = mg.MeasureString("Ag", font, PointF.Empty, ThemeFonts.Tight).Height;

        string? power = null;
        bool charging = false, low = false;
        float boltW = 0f, powerW = 0f;
        if (battery is { } b)
        {
            power = b.State == BatteryState.Charged ? "CHARGED" : b.Percent.ToString(CultureInfo.InvariantCulture) + "%";
            charging = b.State == BatteryState.Charging;
            low = b.State == BatteryState.OnBattery && b.Percent <= 20;
            boltW = charging ? px * 0.7f : 0f;
            powerW = boltW + ThemeFonts.MeasureTracked(mg, power, font, tracking).Width;
        }

        float width = Math.Max(stripW, powerW);
        float height = stripH + (power is not null ? (stripH > 0f ? gap : 0f) + lineH : 0f);
        Point? stripOrigin = track is null ? null : new Point((int)Math.Round((width - stripW) / 2f), 0);
        float powerTop = stripH + (stripH > 0f ? gap : 0f);
        return new SetLayout(width, height, stripOrigin, powerTop, power, charging, low);
    }

    private static void PaintMusicSet(Graphics g, SetLayout set, NowPlayingReading? track, BatteryReading? battery,
                                      GroupInput input, float s, int pad)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float px = GroupThemes.Text(StripPx, s), tracking = px * StripTracking;
        using var font = ThemeFonts.Create(ThemeFonts.Bahn, px);
        float lineH = g.MeasureString("Ag", font, PointF.Empty, ThemeFonts.Tight).Height;

        if (track is { } t && set.StripOrigin is { } origin)
        {
            var stripSave = g.Save();
            g.TranslateTransform(pad + origin.X, pad);
            var inner = MusicStrip.Measure(g, t, input.Source, s);
            float phase = (Environment.TickCount64 % 1500) / 1500f;
            MusicStrip.Paint(g, inner, t, input.TrackArt, s, phase, input.Blur ? 150 : 216);
            g.Restore(stripSave);
        }

        if (battery is { } b && set.Power is { } power)
        {
            var colour = Editorial.HaloColour(b.Percent, b.State, input.Halo, input.Accent);
            bool low = b.State == BatteryState.OnBattery && b.Percent <= 20;
            using var powerBrush = new SolidBrush(low ? colour : Ink);
            float textWidth = ThemeFonts.MeasureTracked(g, power, font, tracking).Width + (b.State == BatteryState.Charging ? px * 0.7f : 0f);
            float px0 = pad + (set.Width - textWidth) / 2f;
            float textY = pad + set.PowerTop;
            if (b.State == BatteryState.Charging)
            {
                BatteryHaloRenderer.PaintBolt(g, px0 + px * 0.3f, textY + lineH / 2f, px * 1.1f, colour);
                px0 += px * 0.7f;
            }
            ThemeFonts.DrawTracked(g, power, font, powerBrush, px0, textY, tracking);
        }
    }
}
