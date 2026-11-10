using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using FeatherWall.Config;

namespace FeatherWall.Widgets.Themes;

/// <summary>Glass dock: everything in one capsule, centred on the top or bottom edge. Left to right,
/// only what is enabled: the turning record, the track with a thin progress bar, the time and the
/// day, the battery ring. Missing segments collapse, and with nothing to show there is no dock.
///
/// The "glass" is a drawn gradient with a hairline highlight and a faint border — never a blur of
/// the wallpaper behind it, which would be a per-frame readback.</summary>
public sealed class GlassDock : IGroupTheme
{
    public static readonly GlassDock Instance = new();

    public ClockAnchor DefaultPosition => ClockAnchor.BottomCenter;

    private const float Record = 76f, Gap = 30f, PadY = 16f, PadLeft = 18f, PadRight = 34f, Edge = 56f;
    private const float TitlePx = 24f, ArtistPx = 18f, BarWidth = 200f, BarHeight = 3f, BarGap = 9f, MaxTrack = 260f;
    private const float TimePx = 56f, DatePx = 15f, Battery = 60f;

    private enum Kind { Record, Track, Separator, Time, Date, Battery }

    public IReadOnlyList<GroupPanel> Layout(GroupInput input)
    {
        float s = Math.Max(input.Scale, 0.05f);
        var track = input.Track;
        var battery = input.Battery;
        bool clock = input.Clock.Enabled;
        if (!clock && track is null && battery is null) return [];

        using var titleFont = ThemeFonts.Create(ThemeFonts.TextSemibold, GroupThemes.Text(TitlePx, s));
        using var artistFont = ThemeFonts.Create(["Segoe UI"], GroupThemes.Text(ArtistPx, s));
        using var timeFont = ThemeFonts.Create(ThemeFonts.DisplayLight, GroupThemes.U(TimePx, s));
        float datePx = GroupThemes.Text(DatePx, s), dateTracking = datePx * 0.14f;
        using var dateFont = ThemeFonts.Create(ThemeFonts.TextSemibold, datePx);

        string title = "", artist = "", time = "", day = "", date = "";
        SizeF titleSize = SizeF.Empty, artistSize = SizeF.Empty, timeSize = SizeF.Empty, daySize = SizeF.Empty, dateSize = SizeF.Empty;
        float maxTrack = GroupThemes.U(MaxTrack, s), bar = GroupThemes.U(BarWidth, s);
        using (var mg = Graphics.FromHwnd(IntPtr.Zero))
        {
            if (track is { } t)
            {
                title = GroupThemes.FitText(mg, t.Title ?? "", titleFont, maxTrack);
                artist = GroupThemes.FitText(mg, t.Artist ?? "", artistFont, maxTrack);
                titleSize = ThemeFonts.MeasureTracked(mg, title, titleFont, 0f);
                artistSize = artist.Length == 0 ? SizeF.Empty : ThemeFonts.MeasureTracked(mg, artist, artistFont, 0f);
            }
            if (clock)
            {
                time = ClockLayout.TimeText(input.Now, input.Clock.TwentyFourHour, input.Clock.ShowSeconds);
                timeSize = ThemeFonts.MeasureTracked(mg, time, timeFont, -0.01f * timeFont.Size);
                if (input.Clock.ShowDate)
                {
                    day = input.Now.ToString("ddd", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
                    date = input.Now.ToString("d MMM", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
                    daySize = ThemeFonts.MeasureTracked(mg, day, dateFont, dateTracking);
                    dateSize = ThemeFonts.MeasureTracked(mg, date, dateFont, dateTracking);
                }
            }
        }

        float record = GroupThemes.U(Record, s), batterySide = GroupThemes.U(Battery, s);
        float barH = Math.Max(GroupThemes.U(BarHeight, s), 1.5f), barGap = GroupThemes.U(BarGap, s);
        float trackW = Math.Max(Math.Max(titleSize.Width, artistSize.Width), bar);
        float trackH = titleSize.Height + artistSize.Height + barGap + barH;
        float dateW = Math.Max(daySize.Width, dateSize.Width), dateH = daySize.Height + dateSize.Height;

        // Segments, grouped so a separator only ever sits between two groups that are both present.
        var groups = new List<List<(Kind Kind, float W, float H)>>();
        if (track is not null) groups.Add([(Kind.Record, record, record), (Kind.Track, trackW, trackH)]);
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
        float padLeft = GroupThemes.U(items[0].Kind == Kind.Record ? PadLeft : PadRight, s);
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
        var faceBox = track is null ? Rectangle.Empty : Rectangle.Round(boxes.First(b => b.Kind == Kind.Record).Box);

        void Paint(Graphics g)
        {
            // Fonts are made again here: the ones Layout measured with are disposed by the time a
            // panel is painted.
            using var titleFont = ThemeFonts.Create(ThemeFonts.TextSemibold, GroupThemes.Text(TitlePx, s));
            using var artistFont = ThemeFonts.Create(["Segoe UI"], GroupThemes.Text(ArtistPx, s));
            using var timeFont = ThemeFonts.Create(ThemeFonts.DisplayLight, GroupThemes.U(TimePx, s));
            using var dateFont = ThemeFonts.Create(ThemeFonts.TextSemibold, datePx);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            PaintCapsule(g, capsule);
            float fade = track is { IsPlaying: false } ? 0.55f : 1f;
            foreach (var (kind, box) in boxes)
            {
                switch (kind)
                {
                    case Kind.Separator:
                        using (var sep = new Pen(Color.FromArgb(31, 255, 255, 255), 1f))
                            g.DrawLine(sep, box.X, capsule.Y + padY + GroupThemes.U(6f, s), box.X, capsule.Bottom - padY - GroupThemes.U(6f, s));
                        break;
                    case Kind.Track:
                        using (var ink = new SolidBrush(Color.FromArgb((int)(245 * fade), 255, 255, 255)))
                            ThemeFonts.DrawTracked(g, title, titleFont, ink, box.X, box.Y, 0f);
                        if (artist.Length > 0)
                            using (var dim = new SolidBrush(Color.FromArgb((int)(158 * fade), 255, 255, 255)))
                                ThemeFonts.DrawTracked(g, artist, artistFont, dim, box.X, box.Y + titleSize.Height, 0f);
                        if (input.Disc.ShowProgress)
                        {
                            var barBox = new RectangleF(box.X, box.Bottom - barH, bar, barH);
                            using (var trackBrush = new SolidBrush(Color.FromArgb(36, 255, 255, 255)))
                                g.FillRectangle(trackBrush, barBox);
                            float p = (float)Math.Clamp(track!.Value.Progress, 0, 1);
                            using var fill = new SolidBrush(GroupThemes.Alpha(input.Accent, fade));
                            if (p > 0) g.FillRectangle(fill, barBox with { Width = barBox.Width * p });
                        }
                        break;
                    case Kind.Time:
                        using (var ink = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
                            ThemeFonts.DrawTracked(g, time, timeFont, ink, box.X, box.Y, -0.01f * timeFont.Size);
                        break;
                    case Kind.Date:
                        using (var accent = new SolidBrush(input.Accent))
                            ThemeFonts.DrawTracked(g, day, dateFont, accent, box.X, box.Y, dateTracking);
                        using (var dim = new SolidBrush(Color.FromArgb(158, 255, 255, 255)))
                            ThemeFonts.DrawTracked(g, date, dateFont, dim, box.X, box.Y + daySize.Height, dateTracking);
                        break;
                    case Kind.Battery:
                        PaintBattery(g, box, input, s);
                        break;
                }
            }
        }

        return [new GroupPanel("dock", new Rectangle(origin, size), faceBox, Paint)];
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
