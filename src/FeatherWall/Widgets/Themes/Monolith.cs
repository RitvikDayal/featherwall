using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using FeatherWall.Config;

namespace FeatherWall.Widgets.Themes;

/// <summary>Monolith: the weekday is the widget. Thin condensed capitals spaced far apart, the date
/// and the time as small tracked lines under it, and the music and battery as one quiet line at the
/// opposite edge. Two panels: the day block at the chosen position, the strip at the other end of
/// the same column, so the pair frames the wallpaper rather than crowding one corner.
///
/// Text aligns to the column it sits in: left, centred or right. Proportions are the 2026-11-10
/// prototype's, through <see cref="GroupThemes.MockupUnit"/>.</summary>
public sealed class Monolith : IGroupTheme
{
    public static readonly Monolith Instance = new();

    public ClockAnchor DefaultPosition => ClockAnchor.TopLeft;

    private const float DayPx = 118f, DayTracking = 0.62f;
    private const float LinePx = 30f, LineTracking = 0.3f, DateGap = 36f, TimeGap = 22f;
    private const float StripPx = 22f, StripTracking = 0.32f, Record = 46f, StripGap = 22f, Rule = 60f, MaxText = 640f;

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
                g => PaintDay(g, lines, s, pad, size.Width - pad * 2, col, input.Accent)));
        }

        var track = input.Track;
        var battery = input.Battery;
        if (track is not null || battery is not null)
        {
            // Capped short of the screen, as Poster caps its record line, so a long title at a large
            // size cannot run the strip off the edge or into the day block.
            var strip = StripLayout(mg, track, battery, s, input.WorkArea.Width * 0.42f);
            var size = new Size((int)Math.Ceiling(strip.Width) + pad * 2, (int)Math.Ceiling(strip.Height) + pad * 2);
            // The opposite end of the same column, or the anchor itself when there is no day block.
            var stripAnchor = panels.Count == 0 ? anchor : (ClockAnchor)((row == 2 ? 0 : 2) * 3 + col);
            var origin = GroupThemes.Place(input.WorkArea, size, stripAnchor, margin);
            var face = track is null ? Rectangle.Empty
                : Rectangle.Round(new RectangleF(pad, pad + (strip.Height - strip.Record) / 2f, strip.Record, strip.Record));
            panels.Add(new GroupPanel("monolith-strip", new Rectangle(origin, size), face,
                g => PaintStrip(g, strip, track, battery, input, s, pad)));
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

    private static void PaintDay(Graphics g, DayText t, float s, int pad, float width, int col, Color accent)
    {
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var dayFont = ThemeFonts.Create(ThemeFonts.BahnCondensedLight, t.DayPx);
        using var dateFont = ThemeFonts.Create(ThemeFonts.BahnSemibold, GroupThemes.Text(LinePx, s));
        using var timeFont = ThemeFonts.Create(ThemeFonts.Bahn, GroupThemes.Text(LinePx, s));
        using var ink = new SolidBrush(Ink);
        using var accentBrush = new SolidBrush(accent);

        float y = pad;
        float Line(string text, Font font, Brush brush, float tracking)
        {
            var size = ThemeFonts.MeasureTracked(g, text, font, tracking);
            float x = pad + col switch { 0 => 0f, 1 => (width - size.Width) / 2f, _ => width - size.Width };
            ThemeFonts.DrawTracked(g, text, font, brush, x, y, tracking);
            return size.Height;
        }

        y += Line(t.Day, dayFont, ink, dayFont.Size * DayTracking);
        if (t.Date is not null)
        {
            y += GroupThemes.U(DateGap, s);
            y += Line(t.Date, dateFont, ink, dateFont.Size * LineTracking);
            y += GroupThemes.U(TimeGap, s);
        }
        else
        {
            y += GroupThemes.U(DateGap, s);
        }
        Line(t.Time, timeFont, accentBrush, timeFont.Size * LineTracking);
    }

    // ---- the strip ---------------------------------------------------------------------------

    private sealed record Strip(float Width, float Height, float Record, string Title, string Artist, string? Power,
                                float TextX, float TitleWidth, float RuleX, float PowerX);

    private static Strip StripLayout(Graphics g, NowPlayingReading? track, BatteryReading? battery, float s, float limit)
    {
        float px = GroupThemes.Text(StripPx, s), tracking = px * StripTracking, gap = GroupThemes.U(StripGap, s);
        using var font = ThemeFonts.Create(ThemeFonts.Bahn, px);
        float lineH = g.MeasureString("Ag", font, PointF.Empty, ThemeFonts.Tight).Height;
        float record = track is null ? 0 : GroupThemes.U(Record, s);

        string? power = battery is { } b
            ? b.State == BatteryState.Charged ? "CHARGED" : b.Percent.ToString(CultureInfo.InvariantCulture) + "%"
            : null;
        float ruleW = power is not null && track is not null ? GroupThemes.U(Rule, s) : 0;
        float boltW = battery is { State: BatteryState.Charging } ? px * 0.7f : 0;
        float powerW = power is null ? 0 : boltW + ThemeFonts.MeasureTracked(g, power, font, tracking).Width;

        string title = "", artist = "";
        float x = 0, titleWidth = 0;
        if (track is { } r)
        {
            var culture = CultureInfo.CurrentCulture;
            title = (r.Title ?? "").ToUpper(culture);
            artist = string.IsNullOrWhiteSpace(r.Artist) ? "" : " · " + r.Artist!.ToUpper(culture);
            x = record + gap;
            // The title gets what the record, the rule and the charge leave of the cap.
            float rest = power is null ? 0 : gap + (ruleW > 0 ? ruleW + gap : 0) + powerW;
            float max = Math.Max(Math.Min(GroupThemes.U(MaxText, s), limit - x - rest), 0);
            title = GroupThemes.FitText(g, title, font, max, tracking);
            float tw = ThemeFonts.MeasureTracked(g, title, font, tracking).Width;
            artist = artist.Length == 0 || tw + tracking >= max ? "" : GroupThemes.FitText(g, artist, font, max - tw - tracking, tracking);
            titleWidth = tw + (artist.Length == 0 ? 0 : tracking + ThemeFonts.MeasureTracked(g, artist, font, tracking).Width);
        }

        float ruleX = x + titleWidth + (titleWidth > 0 ? gap : 0);
        float powerX = ruleX + (ruleW > 0 ? ruleW + gap : 0);

        float width = power is null ? x + titleWidth : powerX + powerW;
        return new Strip(width, Math.Max(record, lineH), record, title, artist, power, x, titleWidth, ruleX, powerX);
    }

    private static void PaintStrip(Graphics g, Strip st, NowPlayingReading? track, BatteryReading? battery,
                                   GroupInput input, float s, int pad)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float px = GroupThemes.Text(StripPx, s), tracking = px * StripTracking;
        using var font = ThemeFonts.Create(ThemeFonts.Bahn, px);
        float lineH = g.MeasureString("Ag", font, PointF.Empty, ThemeFonts.Tight).Height;
        float textY = pad + (st.Height - lineH) / 2f;
        float fade = track is { IsPlaying: false } ? 0.55f : 1f;

        if (track is not null)
        {
            // The record's rim; the face turns on its own layer above this panel.
            var rim = new RectangleF(pad, pad + (st.Height - st.Record) / 2f, st.Record, st.Record);
            float sweep = input.Disc.ShowProgress ? 360f * (float)Math.Clamp(track.Value.Progress, 0, 1) : 0f;
            GroupThemes.Ring(g, RectangleF.Inflate(rim, 1f, 1f), Math.Max(1.5f * s, 1f), Color.Transparent,
                             Color.FromArgb(46, 255, 255, 255), GroupThemes.Alpha(input.Accent, fade), sweep);

            using var ink = new SolidBrush(Color.FromArgb((int)(245 * fade), 255, 255, 255));
            using var dim = new SolidBrush(Color.FromArgb((int)(Dim.A * fade), 255, 255, 255));
            float x = pad + st.TextX;
            ThemeFonts.DrawTracked(g, st.Title, font, ink, x, textY, tracking);
            if (st.Artist.Length > 0)
            {
                x += ThemeFonts.MeasureTracked(g, st.Title, font, tracking).Width + tracking;
                ThemeFonts.DrawTracked(g, st.Artist, font, dim, x, textY, tracking);
            }
        }

        if (battery is not { } b || st.Power is null) return;
        if (track is not null)
        {
            using var rule = new Pen(Color.FromArgb(128, 255, 255, 255), 1f);
            float ry = pad + st.Height / 2f;
            g.DrawLine(rule, pad + st.RuleX, ry, pad + st.RuleX + GroupThemes.U(Rule, s), ry);
        }
        float px0 = pad + st.PowerX;
        var colour = Editorial.HaloColour(b.Percent, b.State, input.Halo, input.Accent);
        if (b.State == BatteryState.Charging)
        {
            BatteryHaloRenderer.PaintBolt(g, px0 + px * 0.3f, pad + st.Height / 2f, px * 1.1f, colour);
            px0 += px * 0.7f;
        }
        bool low = b.State == BatteryState.OnBattery && b.Percent <= input.Halo.LowThreshold;
        using var power = new SolidBrush(low ? colour : Ink);
        ThemeFonts.DrawTracked(g, st.Power, font, power, px0, textY, tracking);
    }
}
