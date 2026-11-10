using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FeatherWall.Widgets.Themes;

/// <summary>The one shadow every new theme shares: the mockup's
/// <c>0 2px 18px rgba(0,0,0,.55), 0 0 2px rgba(0,0,0,.35)</c> at its 1080p authoring scale.
/// The second, tight layer is <see cref="EdgeOpacity"/>, drawn once at each of a glyph's eight
/// neighbouring pixels.</summary>
public readonly record struct ShadowRecipe(float OffsetY, int Blur, float Opacity, float EdgeOpacity = 0f)
{
    public static ShadowRecipe Default => new(2f, 8, 0.55f, 0.2f);

    /// <summary>Room a surface needs on every side so the soft edge reaches zero inside the
    /// bitmap. Clipped, it ends in a hard line — the halo's glow made that mistake once.</summary>
    public int Pad => Blur + (int)Math.Ceiling(Math.Abs(OffsetY));
}

/// <summary>Bakes a soft shadow under whatever a widget has painted.
///
/// GDI+ has no blur. Shrinking the widget's alpha and drawing it back up with bilinear filtering
/// is one: two DrawImage calls per repaint, and nothing at all between repaints. It blurs the
/// widget's own bitmap only — never the wallpaper behind it, which would be a per-frame readback.</summary>
public static class BakedShadow
{
    /// <summary>Replaces <paramref name="target"/>'s contents with the shadow, then the original
    /// content over it. <paramref name="scale"/> multiplies the blur and the offset.
    ///
    /// <paramref name="solidEllipse"/> is an opaque disc drawn on a layer beneath this one — the
    /// record's turning face. It casts a shadow like content, but the shadow is cleared inside it,
    /// because on this layer, above the face, it would darken the artwork.</summary>
    public static void DrawBehind(Bitmap target, ShadowRecipe recipe, float scale, RectangleF? solidEllipse = null)
    {
        int w = target.Width, h = target.Height;
        if (w <= 0 || h <= 0) return;

        float blur = Math.Max(recipe.Blur * Math.Max(scale, 0f), 1f);
        int factor = Math.Max(2, (int)Math.Round(blur / 2f));
        int sw = Math.Max(w / factor, 1), sh = Math.Max(h / factor, 1);

        using var content = (Bitmap)target.Clone();
        using var caster = (Bitmap)target.Clone();
        if (solidEllipse is { } disc)
            using (var g = Graphics.FromImage(caster))
                g.FillEllipse(Brushes.Black, disc);
        using var small = new Bitmap(sw, sh, PixelFormat.Format32bppPArgb);

        // Black, at the content's alpha times the recipe's opacity.
        var toShadow = new ColorMatrix(
        [
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0],
            [0, 0, 0, Math.Clamp(recipe.Opacity, 0f, 1f), 0],
            [0, 0, 0, 0, 1],
        ]);
        using (var g = Graphics.FromImage(small))
        using (var attrs = new ImageAttributes())
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            attrs.SetColorMatrix(toShadow);
            g.DrawImage(caster, new Rectangle(0, 0, sw, sh), 0, 0, w, h, GraphicsUnit.Pixel, attrs);
        }

        using var gt = Graphics.FromImage(target);
        using var clamp = new ImageAttributes();
        // Without clamping, bilinear upscaling samples past the small bitmap's edge and smears a
        // faint line of shadow down the opposite side.
        clamp.SetWrapMode(WrapMode.TileFlipXY);
        gt.Clear(Color.Transparent);
        gt.InterpolationMode = InterpolationMode.HighQualityBilinear;
        gt.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float dy = recipe.OffsetY * Math.Max(scale, 0f);
        gt.DrawImage(small, new Rectangle(0, (int)Math.Round(dy), w, h), 0, 0, sw, sh, GraphicsUnit.Pixel, clamp);
        if (recipe.EdgeOpacity > 0f)
        {
            // The tight layer. The soft one averages a hairline away to nearly nothing; this keeps
            // small text off a background the same colour as the accent it is set in.
            var edge = new ColorMatrix(
            [
                [0, 0, 0, 0, 0],
                [0, 0, 0, 0, 0],
                [0, 0, 0, 0, 0],
                [0, 0, 0, Math.Clamp(recipe.EdgeOpacity, 0f, 1f), 0],
                [0, 0, 0, 0, 1],
            ]);
            using var edgeAttrs = new ImageAttributes();
            edgeAttrs.SetColorMatrix(edge);
            int r = Math.Max(1, (int)Math.Round(Math.Max(scale, 0f)));
            for (int dx = -r; dx <= r; dx += r)
                for (int dyEdge = -r; dyEdge <= r; dyEdge += r)
                    if (dx != 0 || dyEdge != 0)
                        gt.DrawImage(caster, new Rectangle(dx, dyEdge, w, h), 0, 0, w, h, GraphicsUnit.Pixel, edgeAttrs);
        }
        if (solidEllipse is { } face)
        {
            // Inset a pixel, so the face's own anti-aliased edge still sits on shadow, not on a seam.
            var inner = RectangleF.Inflate(face, -1f, -1f);
            var mode = gt.CompositingMode;
            gt.CompositingMode = CompositingMode.SourceCopy;
            using (var clear = new SolidBrush(Color.Transparent))
                gt.FillEllipse(clear, inner);
            gt.CompositingMode = mode;
        }
        gt.DrawImage(content, 0, 0, w, h);
    }
}
