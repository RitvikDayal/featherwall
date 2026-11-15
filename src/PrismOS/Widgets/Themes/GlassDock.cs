using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using PrismOS.Config;

namespace PrismOS.Widgets.Themes;

/// <summary>Glass dock: everything in one frosted capsule. The music pill (art, title, transport)
/// sits left, the clock and date centre, the battery right, separated by hairlines. One panel.</summary>
public sealed class GlassDock : IGroupTheme
{
    public static readonly GlassDock Instance = new();

    public ClockAnchor DefaultPosition => ClockAnchor.BottomCenter;

    private const float TimePx = 74f, DatePx = 15f, Battery = 74f, Gap = 26f, PadY = 22f, PadLeft = 24f, PadRight = 24f, Edge = 30f;

    private enum Kind { Strip, Separator, Time, Date, Battery }

    public IReadOnlyList<GroupPanel> Layout(GroupInput input)
    {
        float s = Math.Max(input.Scale, 0.05f);
        var track = input.Track;
        var battery = input.Battery;
        bool clock = input.Clock.Enabled;
        var strip = track is null ? (GroupThemes.StripLayout?)null : GroupThemes.PaddedStrip(track.Value, input.Source, s);
        if (strip is null && !clock && battery is null) return [];

        using var timeFont = ThemeFonts.Create(ThemeFonts.DisplayLight, GroupThemes.U(TimePx, s));
        float datePx = GroupThemes.Text(DatePx, s), dateTracking = datePx * 0.14f;
        using var dateFont = ThemeFonts.Create(ThemeFonts.TextSemibold, datePx);

        string time = "", day = "", date = "";
        SizeF timeSize = SizeF.Empty, daySize = SizeF.Empty, dateSize = SizeF.Empty;
        float actualTimeWidth = 0f;
        using (var mg = Graphics.FromHwnd(IntPtr.Zero))
        {
            if (clock)
            {
                time = ClockLayout.TimeText(input.Now, input.Clock.TwentyFourHour, input.Clock.ShowSeconds);
                // The slot is sized from the widest digit: proportional digits made the capsule
                // jump width most minutes, and each jump rebuilt the panel.
                char widestDigit = WidestDigit(mg, timeFont);
                foreach (int hour in new[] { 0, 12 })
                {
                    string reference = ClockLayout.TimeText(new DateTime(2000, 1, 1, hour == 0 ? 10 : 22, 58, 58),
                                                            input.Clock.TwentyFourHour, input.Clock.ShowSeconds);
                    string slot = new(reference.Select(c => char.IsDigit(c) ? widestDigit : c).ToArray());
                    var slotSize = ThemeFonts.MeasureTracked(mg, slot, timeFont, -0.01f * timeFont.Size);
                    if (slotSize.Width > timeSize.Width) timeSize = slotSize;
                }
                actualTimeWidth = ThemeFonts.MeasureTracked(mg, time, timeFont, -0.01f * timeFont.Size).Width;
                if (input.Clock.ShowDate)
                {
                    day = input.Now.ToString("ddd", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
                    date = input.Now.ToString("d MMM", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
                    daySize = ThemeFonts.MeasureTracked(mg, day, dateFont, dateTracking);
                    dateSize = ThemeFonts.MeasureTracked(mg, date, dateFont, dateTracking);
                }
            }
        }

        float batterySide = GroupThemes.U(Battery, s);
        float dateW = Math.Max(daySize.Width, dateSize.Width), dateH = daySize.Height + dateSize.Height;

        // Segments, grouped so a separator only ever sits between two present groups.
        var groups = new List<List<(Kind Kind, float W, float H)>>();
        if (strip is { } sm) groups.Add([(Kind.Strip, sm.Size.Width, sm.Size.Height)]);
        if (clock)
        {
            var g = new List<(Kind, float, float)> { (Kind.Time, timeSize.Width, timeSize.Height) };
            if (dateW > 0) g.Add((Kind.Date, dateW, dateH));
            groups.Add(g);
        }
        if (battery is not null) groups.Add([(Kind.Battery, batterySide, batterySide)]);

        var items = new List<(Kind Kind, float W, float H)>();
        for (int i = 0; i < groups.Count; i++)
        {
            if (i > 0) items.Add((Kind.Separator, 1f, 0f));
            items.AddRange(groups[i]);
        }

        float gap = GroupThemes.U(Gap, s), padY = GroupThemes.U(PadY, s);
        float padLeft = GroupThemes.U(items[0].Kind == Kind.Strip ? PadLeft : PadRight, s);
        float padRight = GroupThemes.U(items[^1].Kind == Kind.Battery ? PadLeft : PadRight, s);
        float contentW = items.Sum(it => it.W) + gap * (items.Count - 1);
        float contentH = items.Max(it => it.H);
        var capsuleSize = new SizeF(padLeft + contentW + padRight, contentH + padY * 2);

        int pad = GroupThemes.Pad(s);
        var size = new Size((int)Math.Ceiling(capsuleSize.Width) + pad * 2, (int)Math.Ceiling(capsuleSize.Height) + pad * 2);
        bool top = (int)(input.Position ?? DefaultPosition) / 3 == 0;
        int edge = (int)Math.Round(GroupThemes.U(Edge, s));
        var origin = GroupThemes.Place(input.WorkArea, size, top ? ClockAnchor.TopCenter : ClockAnchor.BottomCenter, edge);

        var capsule = new RectangleF(pad, pad, capsuleSize.Width, capsuleSize.Height);
        float cy = capsule.Y + capsule.Height / 2f;
        var boxes = new List<(Kind Kind, RectangleF Box)>();
        float x = capsule.X + padLeft;
        foreach (var it in items)
        {
            boxes.Add((it.Kind, new RectangleF(x, cy - it.H / 2f, it.W, it.H)));
            x += it.W + gap;
        }

        Rectangle[]? buttons = null;
        if (strip is { } sl)
        {
            var stripBox = boxes.First(item => item.Kind == Kind.Strip).Box;
            buttons =
            [
                new Rectangle(sl.Buttons[0].X + (int)stripBox.X, sl.Buttons[0].Y + (int)stripBox.Y, sl.Buttons[0].Width, sl.Buttons[0].Height),
                new Rectangle(sl.Buttons[1].X + (int)stripBox.X, sl.Buttons[1].Y + (int)stripBox.Y, sl.Buttons[1].Width, sl.Buttons[1].Height),
                new Rectangle(sl.Buttons[2].X + (int)stripBox.X, sl.Buttons[2].Y + (int)stripBox.Y, sl.Buttons[2].Width, sl.Buttons[2].Height),
            ];
        }

        void Paint(Graphics g)
        {
            using var timeFont = ThemeFonts.Create(ThemeFonts.DisplayLight, GroupThemes.U(TimePx, s));
            using var dateFont = ThemeFonts.Create(ThemeFonts.TextSemibold, datePx);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            PaintCapsule(g, capsule);
            foreach (var (kind, box) in boxes)
            {
                switch (kind)
                {
                    case Kind.Separator:
                        using (var sep = new Pen(Color.FromArgb(31, 255, 255, 255), 1f))
                            g.DrawLine(sep, box.X, capsule.Y + padY + GroupThemes.U(6f, s), box.X, capsule.Bottom - padY - GroupThemes.U(6f, s));
                        break;
                    case Kind.Strip:
                        var stripSave = g.Save();
                        g.TranslateTransform(box.X, box.Y);
                        var inner = MusicStrip.Measure(g, track!.Value, input.Source, s);
                        float phase = (Environment.TickCount64 % 100_000) / 100_000f;
                        MusicStrip.Paint(g, inner, track.Value, input.TrackArt, s, phase);
                        g.Restore(stripSave);
                        break;
                    case Kind.Time:
                        using (var ink = new SolidBrush(GroupThemes.ClockInk(input.Clock)))
                            ThemeFonts.DrawTracked(g, time, timeFont, ink, box.X + (box.Width - actualTimeWidth) / 2f, box.Y,
                                                   -0.01f * timeFont.Size);
                        break;
                    case Kind.Date:
                        using (var accent = new SolidBrush(input.Accent))
                            ThemeFonts.DrawTracked(g, day, dateFont, accent, box.X, box.Y, dateTracking);
                        using (var dim = new SolidBrush(GroupThemes.DateInk(input.Clock)))
                            ThemeFonts.DrawTracked(g, date, dateFont, dim, box.X, box.Y + daySize.Height, dateTracking);
                        break;
                    case Kind.Battery:
                        PaintBattery(g, box, input, s);
                        break;
                }
            }
        }

        return [new GroupPanel("dock", new Rectangle(origin, size), Rectangle.Empty, Paint, buttons)];
    }

    private static char WidestDigit(Graphics g, Font font)
    {
        char widest = '0';
        float best = 0f;
        foreach (char d in "0123456789")
        {
            float w = g.MeasureString(d.ToString(), font, PointF.Empty, ThemeFonts.Tight).Width;
            if (w > best) { best = w; widest = d; }
        }
        return widest;
    }

    private static void PaintCapsule(Graphics g, RectangleF r)
    {
        using var path = Capsule(r);
        using (var glass = new LinearGradientBrush(r, Color.FromArgb(158, 28, 22, 40), Color.FromArgb(184, 12, 10, 18), 90f))
            g.FillPath(glass, path);
        using (var border = new Pen(Color.FromArgb(15, 255, 255, 255), 1f))
            g.DrawPath(border, path);
        // The hairline highlight along the top edge, inside the curve.
        var state = g.Save();
        g.SetClip(path);
        using (var highlight = new Pen(Color.FromArgb(36, 255, 255, 255), 1f))
            g.DrawLine(highlight, r.X + r.Height / 2f, r.Y + 1f, r.Right - r.Height / 2f, r.Y + 1f);
        g.Restore(state);
    }

    private static GraphicsPath Capsule(RectangleF r)
    {
        var path = new GraphicsPath();
        float d = r.Height;
        path.AddArc(r.X, r.Y, d, d, 90, 180);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        path.CloseFigure();
        return path;
    }

    private static void PaintBattery(Graphics g, RectangleF box, GroupInput input, float s)
    {
        var r = input.Battery!.Value;
        float k = box.Width / Battery;
        var ring = RectangleF.Inflate(box, -5f * k, -5f * k);
        var colour = Editorial.HaloColour(r.Percent, r.State, input.Halo, input.Accent);
        float sweep = r.State == BatteryState.Charged ? 360f : 360f * Math.Clamp(r.Percent, 0, 100) / 100f;
        GroupThemes.Ring(g, ring, Math.Max(4f * k, 1.2f), Color.Transparent, Color.FromArgb(41, 255, 255, 255), colour, sweep);
        GroupThemes.BatteryCentre(g, new PointF(box.X + box.Width / 2f, box.Y + box.Height / 2f), box.Width, r, colour,
                                  ThemeFonts.TextSemibold, GroupThemes.Text(16f, s) * 0.85f, GroupThemes.Text(10f, s) * 0.6f);
    }
}
