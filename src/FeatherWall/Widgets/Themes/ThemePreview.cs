using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using FeatherWall.Config;

namespace FeatherWall.Widgets.Themes;

/// <summary>The Style page's tiles: each theme drawn by its own renderers over a miniature
/// desktop, with sample data. A preview drawn by separate code would drift from what ships.
///
/// The miniature desktop is 960×540 rather than a real screen's size, so the widgets are large
/// enough to read once the tile is scaled down to a thumbnail.</summary>
public static class ThemePreview
{
    private static readonly Size Canvas = new(960, 540);
    private const int Margin = 28;
    private static readonly Color StandIn = Color.FromArgb(255, 0x2B, 0x32, 0x40);

    private static readonly DateTime SampleTime = new(2026, 11, 10, 12, 30, 0);
    private static readonly NowPlayingReading SampleTrack = new("Bairan", "Banjaare", true, "preview", 0.42);
    private static readonly BatteryReading SampleBattery = new(76, BatteryState.OnBattery);

    public static Bitmap Render(WidgetTheme theme, Size tile, Color accent, Image? backdrop)
    {
        using var canvas = new Bitmap(Canvas.Width, Canvas.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (backdrop is null) g.Clear(StandIn);
            else DrawCover(g, backdrop);

            switch (theme)
            {
                case WidgetTheme.Classic: DrawClassic(g); break;
                case WidgetTheme.Editorial: DrawEditorial(g, accent); break;
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

    private static void DrawClassic(Graphics g)
    {
        var clock = new ClockConfig();
        var cm = ClockRenderer.Measure(clock, SampleTime);
        Place(g, cm.Total, (Canvas.Width - cm.Total.Width) / 2, Margin,
              bg => ClockRenderer.Paint(bg, clock, cm, ClockRenderer.ParseColor(clock.Color), cm.Total), shadow: 0f);

        var disc = new DiscConfig();
        var rm = NowPlayingRenderer.Measure(disc, SampleTrack);
        using var face = DiscRenderer.RenderFace(Math.Max(rm.DiscBox.Width, 1), null, ClockRenderer.ParseColor(disc.AccentColor));
        Place(g, rm.Total, Margin, Canvas.Height - Margin - rm.Total.Height,
              bg => NowPlayingRenderer.Paint(bg, rm, disc, SampleTrack, face, 0.1f, (float)SampleTrack.Progress), shadow: 0f);

        var halo = new HaloConfig();
        var hs = BatteryHaloRenderer.Measure(halo, SampleBattery);
        Place(g, hs, Canvas.Width - Margin - hs.Width, Margin,
              bg => BatteryHaloRenderer.Paint(bg, new Rectangle(Point.Empty, hs), halo, SampleBattery), shadow: 0f);
    }

    private static void DrawEditorial(Graphics g, Color accent)
    {
        const float scale = 1f;
        var cm = Editorial.MeasureClock(new ClockConfig(), SampleTime, scale);
        Place(g, cm.Total, (Canvas.Width - cm.Total.Width) / 2, Margin / 2,
              bg => Editorial.PaintClock(bg, cm, accent), scale);

        var disc = new DiscConfig();
        var rm = Editorial.MeasureRecord(disc, SampleTrack, scale);
        using var face = DiscRenderer.RenderFace(Math.Max(rm.DiscBox.Width, 1), null, accent);
        Place(g, rm.Total, Margin / 2, Canvas.Height - Margin / 2 - rm.Total.Height, bg =>
        {
            DiscRenderer.PaintFace(bg, rm.DiscBox, face, 0.1f, true);
            Editorial.PaintRecordStatic(bg, rm, disc, SampleTrack, (float)SampleTrack.Progress, accent, scale);
        }, scale);

        var halo = new HaloConfig();
        var hm = Editorial.MeasureInfo(new InfoConfig(), [], scale, Editorial.MeasureHalo(halo, SampleBattery, scale));
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
