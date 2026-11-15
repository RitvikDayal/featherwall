using System.Drawing.Drawing2D;

namespace PrismOS.Tray;

/// <summary>The PrismOS mark: a cut diamond, faceted from the upper left.
///
/// Drawn rather than loaded, so it is crisp at every size the shell asks for — the notification
/// area renders it at 16 pixels and the shell scales the same handle up for large icons — and so
/// the tray icon and the application's .ico are the same shape by construction rather than by
/// two artists agreeing. The .ico is generated from this code by scripts/icon/IconGen.
///
/// The old mark was a feather. A feather reads as "light and weight", which was true of the
/// project it came from and is not true of this one; a cut gem is the same idea drawn
/// geometrically, and it survives being shrunk to 16 pixels, which a feather did not.</summary>
public static class PrismIcon
{
    /// <summary>The two ends of the gradient. Blue into violet: the same family the old mark
    /// used, so the tray keeps its colour continuity across the rename.</summary>
    public static readonly Color Light = Color.FromArgb(255, 0x5A, 0xA9, 0xFF);
    public static readonly Color Dark = Color.FromArgb(255, 0x7C, 0x4D, 0xFF);

    /// <summary>Draws the mark, centred, filling <paramref name="size"/> x <paramref name="size"/>
    /// pixels. The caller owns the graphics and the surface.</summary>
    public static void Draw(Graphics g, int size)
    {
        if (size <= 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        float pad = size * 0.10f;
        float cx = size / 2f;
        float cy = size / 2f;
        float hw = cx - pad;
        float hh = cy - pad;

        var top = new PointF(cx, cy - hh);
        var right = new PointF(cx + hw, cy);
        var bottom = new PointF(cx, cy + hh);
        var left = new PointF(cx - hw, cy);
        var centre = new PointF(cx, cy);

        // The body.
        using (var body = new GraphicsPath())
        {
            body.AddPolygon([top, right, bottom, left]);
            using var brush = new LinearGradientBrush(
                new PointF(cx - hw, cy - hh), new PointF(cx + hw, cy + hh), Light, Dark);
            g.FillPath(brush, body);
        }

        // Four facets, lit from the upper left. Two lighter, two darker, so the mark reads as a
        // solid rather than a flat rhombus even at 16 pixels.
        using (var lightFacet = new SolidBrush(Color.FromArgb(105, 255, 255, 255)))
        using (var softFacet = new SolidBrush(Color.FromArgb(58, 255, 255, 255)))
        using (var darkFacet = new SolidBrush(Color.FromArgb(32, 0, 0, 0)))
        using (var deepFacet = new SolidBrush(Color.FromArgb(66, 0, 0, 0)))
        {
            g.FillPolygon(lightFacet, [top, left, centre]);
            g.FillPolygon(softFacet, [top, centre, right]);
            g.FillPolygon(darkFacet, [left, bottom, centre]);
            g.FillPolygon(deepFacet, [bottom, right, centre]);
        }

        // The outline: what keeps the mark legible against a light taskbar.
        float stroke = Math.Max(1f, size * 0.055f);
        using (var outline = new Pen(Color.FromArgb(225, 255, 255, 255), stroke) { LineJoin = LineJoin.Round })
        using (var path = new GraphicsPath())
        {
            path.AddPolygon([top, right, bottom, left]);
            g.DrawPath(outline, path);
        }
    }

    /// <summary>Draws the mark into a new 32-bit ARGB bitmap with a transparent background.</summary>
    public static Bitmap ToBitmap(int size)
    {
        var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        Draw(g, size);
        return bitmap;
    }

    /// <summary>The mark as an icon, for a window's title bar. The returned icon owns its own
    /// handle: Icon.FromHandle does not, so the original would be freed with the temporary.</summary>
    public static Icon CreateIcon(int size = 32)
    {
        using var bitmap = ToBitmap(size);
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
