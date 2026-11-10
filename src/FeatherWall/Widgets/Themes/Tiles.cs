using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using FeatherWall.Config;

namespace FeatherWall.Widgets.Themes;

/// <summary>Tiles: rounded cards with a flat dark tint, never a blur. A scalloped analog clock and a
/// date card side by side, then a wide music card with the turning record and a wide battery card.
/// One panel; cards that have nothing to show are left out and the stack closes up.
///
/// The clock is the only analog face in the set. Its hands move at the clock's existing repaint, so
/// it adds no timer. Proportions are the 2026-11-10 prototype's, through
/// <see cref="GroupThemes.MockupUnit"/>.</summary>
public sealed class Tiles : IGroupTheme
{
    public static readonly Tiles Instance = new();

    public ClockAnchor DefaultPosition => ClockAnchor.BottomRight;

    private const float Col = 250f, Gap = 18f, Radius = 34f, Inner = 26f;
    private const float Cookie = 198f, MusicRecord = 110f, BatteryRing = 90f;
    private const float LabelPx = 18f, BigPx = 56f, BatteryPx = 48f, TitlePx = 28f, ArtistPx = 20f, BarH = 6f, BarGap = 14f;

    private static readonly Color Card = Color.FromArgb(184, 18, 20, 26);
    private static readonly Color Edge = Color.FromArgb(18, 255, 255, 255);
    private static readonly Color Ink = Color.FromArgb(245, 255, 255, 255);
    private static readonly Color Label = Color.FromArgb(140, 255, 255, 255);

    /// <summary>Hour and minute hand angles in degrees clockwise from twelve.</summary>
    public static (float Hour, float Minute) HandAngles(DateTime now) =>
        ((now.Hour % 12 + now.Minute / 60f) * 30f, (now.Minute + now.Second / 60f) * 6f);

    public IReadOnlyList<GroupPanel> Layout(GroupInput input)
    {
        float s = Math.Max(input.Scale, 0.05f);
        bool clock = input.Clock.Enabled, date = clock && input.Clock.ShowDate;
        var track = input.Track;
        var battery = input.Battery;
        if (!clock && track is null && battery is null) return [];

        float col = GroupThemes.U(Col, s), gap = GroupThemes.U(Gap, s), inner = GroupThemes.U(Inner, s);
        float width = col * 2 + gap;
        float musicH = GroupThemes.U(MusicRecord, s) + inner * 2;
        float batteryH = GroupThemes.U(BatteryRing, s) + inner * 2;

        var cards = new List<(string Kind, RectangleF Box)>();
        float y = 0;
        if (clock)
        {
            cards.Add(("clock", new RectangleF(0, y, col, col)));
            if (date) cards.Add(("date", new RectangleF(col + gap, y, col, col)));
            y += col + gap;
        }
        if (track is not null) { cards.Add(("music", new RectangleF(0, y, width, musicH))); y += musicH + gap; }
        if (battery is not null) { cards.Add(("battery", new RectangleF(0, y, width, batteryH))); y += batteryH + gap; }
        float height = y - gap;

        int pad = GroupThemes.Pad(s);
        var size = new Size((int)Math.Ceiling(width) + pad * 2, (int)Math.Ceiling(height) + pad * 2);
        var origin = GroupThemes.Place(input.WorkArea, size, input.Position ?? DefaultPosition, GroupThemes.Margin(input));
        for (int i = 0; i < cards.Count; i++)
            cards[i] = (cards[i].Kind, new RectangleF(cards[i].Box.X + pad, cards[i].Box.Y + pad, cards[i].Box.Width, cards[i].Box.Height));

        var faceBox = Rectangle.Empty;
        if (track is not null)
        {
            var music = cards.First(c => c.Kind == "music").Box;
            float rec = GroupThemes.U(MusicRecord, s);
            faceBox = Rectangle.Round(new RectangleF(music.X + inner, music.Y + inner, rec, rec));
        }

        void Paint(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            foreach (var (kind, box) in cards)
            {
                PaintCard(g, box, s);
                switch (kind)
                {
                    case "clock": PaintClock(g, box, input, s); break;
                    case "date": PaintDate(g, box, input, s, inner); break;
                    case "music": PaintMusic(g, box, input, s, inner, faceBox); break;
                    case "battery": PaintBattery(g, box, input, s, inner); break;
                }
            }
        }

        return [new GroupPanel("tiles", new Rectangle(origin, size), faceBox, Paint)];
    }

    private static void PaintCard(Graphics g, RectangleF box, float s)
    {
        using var path = Rounded(box, GroupThemes.U(Radius, s));
        using (var fill = new SolidBrush(Card)) g.FillPath(fill, path);
        using var edge = new Pen(Edge, 1f);
        g.DrawPath(edge, path);
    }

    private static void PaintClock(Graphics g, RectangleF box, GroupInput input, float s)
    {
        float side = GroupThemes.U(Cookie, s), k = side / 200f;
        float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f;

        // The prototype's "cookie": a circle with nine soft scallops.
        var points = new List<PointF>();
        for (int a = 0; a < 360; a += 2)
        {
            double rad = a * Math.PI / 180, r = (86 + 6 * Math.Cos(rad * 9)) * k;
            points.Add(new PointF(cx + (float)(r * Math.Sin(rad)), cy - (float)(r * Math.Cos(rad))));
        }
        using (var face = new SolidBrush(Color.FromArgb(20, 255, 255, 255))) g.FillPolygon(face, points.ToArray());
        using (var rim = new Pen(Color.FromArgb(64, 255, 255, 255), Math.Max(1.5f * k, 1f))) g.DrawPolygon(rim, points.ToArray());

        var (hour, minute) = HandAngles(input.Now);
        Hand(g, cx, cy, hour, 46 * k, Math.Max(7 * k, 2f), Ink);
        Hand(g, cx, cy, minute, 64 * k, Math.Max(4 * k, 1.5f), input.Accent);
        if (input.Clock.ShowSeconds)
            Hand(g, cx, cy, input.Now.Second * 6f, 70 * k, Math.Max(1.5f * k, 1f), Color.FromArgb(160, 255, 255, 255));
        float dot = 6 * k;
        using var centre = new SolidBrush(input.Accent);
        g.FillEllipse(centre, cx - dot, cy - dot, dot * 2, dot * 2);
    }

    private static void Hand(Graphics g, float cx, float cy, float degrees, float length, float width, Color colour)
    {
        double rad = degrees * Math.PI / 180;
        using var pen = new Pen(colour, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, cx, cy, cx + (float)(length * Math.Sin(rad)), cy - (float)(length * Math.Cos(rad)));
    }

    private static void PaintDate(Graphics g, RectangleF box, GroupInput input, float s, float inner)
    {
        var culture = CultureInfo.CurrentCulture;
        float labelPx = GroupThemes.Text(LabelPx, s), labelTracking = labelPx * 0.12f;
        using var labelFont = ThemeFonts.Create(ThemeFonts.TextSemibold, labelPx);
        using var bigFont = ThemeFonts.Create(ThemeFonts.DisplaySemibold, GroupThemes.U(BigPx, s));
        using var label = new SolidBrush(Label);
        using var ink = new SolidBrush(Ink);

        float x = box.X + inner, y = box.Y + inner, maxW = box.Width - inner * 2;
        string day = GroupThemes.FitText(g, input.Now.ToString("dddd", culture).ToUpper(culture), labelFont, maxW, labelTracking);
        ThemeFonts.DrawTracked(g, day, labelFont, label, x, y, labelTracking);
        y += ThemeFonts.MeasureTracked(g, day, labelFont, labelTracking).Height + GroupThemes.U(10f, s);
        string number = input.Now.Day.ToString(culture);
        g.DrawString(number, bigFont, ink, x, y, ThemeFonts.Tight);
        y += g.MeasureString(number, bigFont, PointF.Empty, ThemeFonts.Tight).Height + GroupThemes.U(8f, s);
        string month = GroupThemes.FitText(g, input.Now.ToString("MMMM", culture).ToUpper(culture), labelFont, maxW, labelTracking);
        ThemeFonts.DrawTracked(g, month, labelFont, label, x, y, labelTracking);
    }

    private static void PaintMusic(Graphics g, RectangleF box, GroupInput input, float s, float inner, Rectangle face)
    {
        var r = input.Track!.Value;
        float fade = r.IsPlaying ? 1f : 0.55f;
        float x = face.Right + GroupThemes.U(22f, s), right = box.Right - inner;
        float maxW = right - x;
        using var titleFont = ThemeFonts.Create(ThemeFonts.DisplaySemibold, GroupThemes.Text(TitlePx, s));
        using var artistFont = ThemeFonts.Create(ThemeFonts.Text, GroupThemes.Text(ArtistPx, s));
        string title = GroupThemes.FitText(g, r.Title ?? "", titleFont, maxW);
        string artist = GroupThemes.FitText(g, r.Artist ?? "", artistFont, maxW);
        var ts = g.MeasureString(title.Length == 0 ? "Ag" : title, titleFont, PointF.Empty, ThemeFonts.Tight);
        var asz = g.MeasureString(artist.Length == 0 ? "Ag" : artist, artistFont, PointF.Empty, ThemeFonts.Tight);
        float barH = GroupThemes.U(BarH, s), barGap = GroupThemes.U(BarGap, s);
        float block = ts.Height + asz.Height + (input.Disc.ShowProgress ? barGap + barH : 0);
        float y = box.Y + (box.Height - block) / 2f;

        using (var ink = new SolidBrush(Color.FromArgb((int)(245 * fade), 255, 255, 255)))
            g.DrawString(title, titleFont, ink, x, y, ThemeFonts.Tight);
        using (var dim = new SolidBrush(Color.FromArgb((int)(153 * fade), 255, 255, 255)))
            g.DrawString(artist, artistFont, dim, x, y + ts.Height, ThemeFonts.Tight);
        if (!input.Disc.ShowProgress) return;

        var bar = new RectangleF(x, y + ts.Height + asz.Height + barGap, maxW, barH);
        using (var track = new SolidBrush(Color.FromArgb(31, 255, 255, 255)))
        using (var path = Rounded(bar, barH / 2f)) g.FillPath(track, path);
        float done = bar.Width * (float)Math.Clamp(r.Progress, 0, 1);
        if (done < barH) return;
        using var accent = new SolidBrush(GroupThemes.Alpha(input.Accent, fade));
        using var filled = Rounded(bar with { Width = done }, barH / 2f);
        g.FillPath(accent, filled);
    }

    private static void PaintBattery(Graphics g, RectangleF box, GroupInput input, float s, float inner)
    {
        var r = input.Battery!.Value;
        float side = GroupThemes.U(BatteryRing, s), stroke = side * 6f / 60f;
        var ring = new RectangleF(box.X + inner + stroke / 2f, box.Y + inner + stroke / 2f, side - stroke, side - stroke);
        var colour = Editorial.HaloColour(r.Percent, r.State, input.Halo, input.Accent);
        float sweep = r.State == BatteryState.Charged ? 360f : 360f * Math.Clamp(r.Percent, 0, 100) / 100f;
        GroupThemes.Ring(g, ring, stroke, Color.Transparent, Color.FromArgb(36, 255, 255, 255), colour, sweep);
        if (r.State == BatteryState.Charging)
            BatteryHaloRenderer.PaintBolt(g, ring.X + ring.Width / 2f, ring.Y + ring.Height / 2f, side * 0.42f, colour);

        float labelPx = GroupThemes.Text(LabelPx, s), labelTracking = labelPx * 0.12f;
        using var labelFont = ThemeFonts.Create(ThemeFonts.TextSemibold, labelPx);
        using var bigFont = ThemeFonts.Create(ThemeFonts.DisplaySemibold, GroupThemes.U(BatteryPx, s));
        string word = r.State switch
        {
            BatteryState.Charging => "CHARGING",
            BatteryState.Charged => "CHARGED",
            _ => "BATTERY",
        };
        string value = r.Percent.ToString(CultureInfo.InvariantCulture) + "%";
        var ls = ThemeFonts.MeasureTracked(g, word, labelFont, labelTracking);
        var vs = g.MeasureString(value, bigFont, PointF.Empty, ThemeFonts.Tight);
        float x = box.X + inner + side + GroupThemes.U(22f, s);
        float y = box.Y + (box.Height - ls.Height - GroupThemes.U(6f, s) - vs.Height) / 2f;
        using var label = new SolidBrush(Label);
        using var ink = new SolidBrush(Ink);
        ThemeFonts.DrawTracked(g, word, labelFont, label, x, y, labelTracking);
        g.DrawString(value, bigFont, ink, x, y + ls.Height + GroupThemes.U(6f, s), ThemeFonts.Tight);
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
