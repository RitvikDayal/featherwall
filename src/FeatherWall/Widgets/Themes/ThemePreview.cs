using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using FeatherWall.Config;

namespace FeatherWall.Widgets.Themes;

/// <summary>The Style page's tiles: each theme drawn by its own renderers over a miniature
/// desktop, with sample data and the user's own settings. A preview drawn by separate code would
/// drift from what ships, and one drawn with default settings would not show their Classic.
///
/// The miniature desktop is 640×360 with the widgets at 0.8, so they fill the thumbnail and the
/// themes are told apart at a glance. At a real screen's proportions both read as a small
/// "12:30" in a large picture.</summary>
public static class ThemePreview
{
    private static readonly Size Canvas = new(640, 360);
    private const int Margin = 18;
    private const float WidgetScale = 0.8f;

    public static Size CanvasSize => Canvas;
    public static int MarginPx => Margin;

    /// <summary>The largest scale, up to the preview's 0.8, at which the clock and the record stack
    /// without overlapping and the clock clears the battery beside it. Sizes are measured at 1.
    /// Without this a large record — 260 px on Ritvik's desktop — drew over the clock.</summary>
    public static float Fit(Size clock, Size record, Size halo)
    {
        float vertical = (Canvas.Height - Margin * 3f) / Math.Max(clock.Height + record.Height, 1);
        float horizontal = (Canvas.Width - Margin * 4f - halo.Width * 2f) / Math.Max(clock.Width, 1);
        return Math.Clamp(Math.Min(vertical, horizontal), 0.05f, WidgetScale);
    }
    private static readonly Color StandIn = Color.FromArgb(255, 0x2B, 0x32, 0x40);

    private static readonly DateTime SampleTime = new(2026, 11, 10, 12, 30, 0);
    private static readonly NowPlayingReading SampleTrack = new("Bairan", "Banjaare", true, "preview", 0.42);
    private static readonly BatteryReading SampleBattery = new(76, BatteryState.OnBattery);

    public static Bitmap Render(WidgetTheme theme, Size tile, Color accent, Image? backdrop, AppConfig? config = null)
    {
        config ??= new AppConfig();
        using var canvas = new Bitmap(Canvas.Width, Canvas.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (backdrop is null) g.Clear(StandIn);
            else DrawCover(g, backdrop);

            switch (theme)
            {
                case WidgetTheme.Classic: DrawClassic(g, config); break;
                case WidgetTheme.Editorial: DrawEditorial(g, accent, config); break;
                // A theme with no preview fails EveryTheme_Renders rather than shipping a tile
                // that quietly shows Classic under the new theme's name.
                default: throw new NotSupportedException($"No Style page preview for {theme}");
            }
        }

        var result = new Bitmap(tile.Width, tile.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(canvas, new Rectangle(Point.Empty, tile));
        }
        return result;
    }

    private static void DrawClassic(Graphics g, AppConfig config)
    {
        var clock = config.Clock;
        var disc = config.Info.Disc;
        var halo = config.Info.Halo;
        float scale = Fit(ClockRenderer.Measure(clock, SampleTime).Total,
                          NowPlayingRenderer.Measure(disc, SampleTrack).Total,
                          BatteryHaloRenderer.Measure(halo, SampleBattery));

        var cm = ClockRenderer.Measure(clock, SampleTime, scale);
        Place(g, cm.Total, (Canvas.Width - cm.Total.Width) / 2, Margin,
              bg => ClockRenderer.Paint(bg, clock, cm, ClockRenderer.ParseColor(clock.Color), cm.Total), shadow: 0f);

        var rm = NowPlayingRenderer.Measure(disc, SampleTrack, scale);
        using var face = DiscRenderer.RenderFace(Math.Max(rm.DiscBox.Width, 1), null, ClockRenderer.ParseColor(disc.AccentColor));
        Place(g, rm.Total, Margin, Canvas.Height - Margin - rm.Total.Height,
              bg => NowPlayingRenderer.Paint(bg, rm, disc, SampleTrack, face, 0.1f, (float)SampleTrack.Progress, scale), shadow: 0f);

        var hs = BatteryHaloRenderer.Measure(halo, SampleBattery, scale);
        Place(g, hs, Canvas.Width - Margin - hs.Width, Margin,
              bg => BatteryHaloRenderer.Paint(bg, new Rectangle(Point.Empty, hs), halo, SampleBattery), shadow: 0f);
    }

    private static void DrawEditorial(Graphics g, Color accent, AppConfig config)
    {
        var disc = config.Info.Disc;
        var halo = config.Info.Halo;
        float unit = Editorial.Scale(config.Clock, 1.0);
        float scale = unit * Fit(Editorial.MeasureClock(config.Clock, SampleTime, unit).Total,
                                 Editorial.MeasureRecord(disc, SampleTrack, unit).Total,
                                 Editorial.MeasureHalo(halo, SampleBattery, unit));
        var cm = Editorial.MeasureClock(config.Clock, SampleTime, scale);
        Place(g, cm.Total, (Canvas.Width - cm.Total.Width) / 2, Margin / 2,
              bg => Editorial.PaintClock(bg, cm, accent), scale);

        var rm = Editorial.MeasureRecord(disc, SampleTrack, scale);
        using var face = DiscRenderer.RenderFace(Math.Max(rm.DiscBox.Width, 1), null, accent);
        Place(g, rm.Total, Margin / 2, Canvas.Height - Margin / 2 - rm.Total.Height, bg =>
        {
            DiscRenderer.PaintFace(bg, rm.DiscBox, face, 0.1f, true);
            Editorial.PaintRecordStatic(bg, rm, disc, SampleTrack, (float)SampleTrack.Progress, accent, scale);
        }, scale);

        var hm = Editorial.MeasureInfo(config.Info, [], scale, Editorial.MeasureHalo(halo, SampleBattery, scale));
        Place(g, hm.Total, Canvas.Width - Margin / 2 - hm.Total.Width, Margin / 2,
              bg => Editorial.PaintHalo(bg, hm.HaloBox, halo, SampleBattery, accent), scale);
    }

    /// <summary>Paints one widget into its own surface, exactly as its overlay does, then places it.
    /// <paramref name="shadow"/> above zero bakes the themes' shadow at that scale.</summary>
    private static void Place(Graphics g, Size size, int x, int y, Action<Graphics> paint, float shadow)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        using var surface = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using (var sg = Graphics.FromImage(surface)) paint(sg);
        if (shadow > 0f) BakedShadow.DrawBehind(surface, ShadowRecipe.Default, shadow);
        g.DrawImage(surface, x, y, size.Width, size.Height);
    }

    private static void DrawCover(Graphics g, Image image)
    {
        float s = Math.Max((float)Canvas.Width / image.Width, (float)Canvas.Height / image.Height);
        float w = image.Width * s, h = image.Height * s;
        g.DrawImage(image, (Canvas.Width - w) / 2f, (Canvas.Height - h) / 2f, w, h);
    }
}
