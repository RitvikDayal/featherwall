using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using PrismOS.Config;

namespace PrismOS.Widgets.Themes;

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
    private const float Cookie = 198f, BatteryRing = 90f;
    private const float LabelPx = 18f, BigPx = 56f, BatteryPx = 48f;

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
        float batteryH = GroupThemes.U(BatteryRing, s) + inner * 2;
        var strip = track is null ? (GroupThemes.StripLayout?)null : GroupThemes.PaddedStrip(track.Value, input.Source, s);
        float stripScale = s;
        if (strip is { } fit && width < fit.Metrics.Size.Width)
        {
            stripScale = s * (width / fit.Metrics.Size.Width);
            strip = GroupThemes.PaddedStrip(track!.Value, input.Source, stripScale);
        }

        var cards = new List<(string Kind, RectangleF Box)>();
        float y = 0;
        if (clock)
        {
            cards.Add(("clock", new RectangleF(0, y, col, col)));
            if (date) cards.Add(("date", new RectangleF(col + gap, y, col, col)));
            y += col + gap;
        }
        if (strip is { } sl) { cards.Add(("music", new RectangleF(0, y, width, sl.Size.Height))); y += sl.Size.Height + gap; }
        if (battery is not null) { cards.Add(("battery", new RectangleF(0, y, width, batteryH))); y += batteryH + gap; }
        float height = y - gap;

        int pad = GroupThemes.Pad(s);
        var size = new Size((int)Math.Ceiling(width) + pad * 2, (int)Math.Ceiling(height) + pad * 2);
        var origin = GroupThemes.Place(input.WorkArea, size, input.Position ?? DefaultPosition, GroupThemes.Margin(input));
        for (int i = 0; i < cards.Count; i++)
            cards[i] = (cards[i].Kind, new RectangleF(cards[i].Box.X + pad, cards[i].Box.Y + pad, cards[i].Box.Width, cards[i].Box.Height));

        Rectangle[]? buttons = null;
        float stripInnerX = 0f;
        if (strip is { } stripLayout)
        {
            var slot = cards.First(c => c.Kind == "music").Box;
            stripInnerX = (slot.Width - stripLayout.Metrics.Size.Width) / 2f;
            buttons =
            [
                Shift(stripLayout.Buttons[0], (int)(slot.X + stripInnerX), (int)slot.Y),
                Shift(stripLayout.Buttons[1], (int)(slot.X + stripInnerX), (int)slot.Y),
                Shift(stripLayout.Buttons[2], (int)(slot.X + stripInnerX), (int)slot.Y),
            ];
        }

        void Paint(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            foreach (var (kind, box) in cards)
            {
                PaintCard(g, box, s, input.Blur);
                switch (kind)
                {
                    case "clock": PaintClock(g, box, input, s); break;
                    case "date": PaintDate(g, box, input, s, inner); break;
                    case "music":
                        var musicSave = g.Save();
                        g.TranslateTransform(box.X + stripInnerX, box.Y);
                        var innerStrip = MusicStrip.Measure(g, track!.Value, input.Source, stripScale);
                        float phase = Environment.TickCount64 / 1000f;
                        MusicStrip.Paint(g, innerStrip, track.Value, input.TrackArt, stripScale, phase, input.Blur ? 150 : 216, input.TrackLevels);
                        g.Restore(musicSave);
                        break;
                    case "battery": PaintBattery(g, box, input, s, inner); break;
                }
            }
        }

        IReadOnlyList<PanelBlur.Region>? cardRegions = input.Blur
            ? cards.Select(c => new PanelBlur.Region(c.Box, GroupThemes.U(Radius, s))).ToArray()
            : null;

        return [new GroupPanel("tiles", new Rectangle(origin, size), Rectangle.Empty, Paint, buttons, cardRegions)];
    }

    private static void PaintCard(Graphics g, RectangleF box, float s, bool blur)
    {
        using var path = Rounded(box, GroupThemes.U(Radius, s));
        using (var fill = new SolidBrush(Color.FromArgb(blur ? 120 : 184, 18, 20, 26))) g.FillPath(fill, path);
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
        Hand(g, cx, cy, hour, 46 * k, Math.Max(7 * k, 2f), GroupThemes.ClockInk(input.Clock));
        Hand(g, cx, cy, minute, 64 * k, Math.Max(4 * k, 1.5f), input.Accent);
        if (input.Clock.ShowSeconds)
            Hand(g, cx, cy, input.Now.Second * 6f, 70 * k, Math.Max(1.5f * k, 1f), GroupThemes.Dim(GroupThemes.ClockInk(input.Clock), 0.63f));
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
        using var label = new SolidBrush(GroupThemes.Dim(GroupThemes.DateInk(input.Clock), 0.55f));
        using var ink = new SolidBrush(GroupThemes.DateInk(input.Clock));

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

    private static Rectangle Shift(Rectangle r, int dx, int dy) => new(r.X + dx, r.Y + dy, r.Width, r.Height);

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
