using System.Drawing;
using System.Drawing.Drawing2D;
using FeatherWall.Config;

namespace FeatherWall.Widgets;

/// <summary>Draws the now-playing record: album art on the label, grooves, a highlight sweep, and
/// the track's progress on the rim.
///
/// The face — everything that turns — is rendered once per track by <see cref="RenderFace"/> and
/// then drawn through a rotation transform each frame. Redrawing the artwork and forty groove
/// circles per frame would be the difference between a rounding error and a real cost.
///
/// The progress ring does not rotate. A turning progress arc is unreadable.</summary>
public static class DiscRenderer
{
    private const int MinSize = 24;
    private const float LabelRatio = 0.40f;
    private const float SpindleRatio = 0.055f;

    /// <summary>Empty means draw nothing: switched off, or nothing to show. A paused track still
    /// measures — it stays on screen, dimmed and still, because it is still what you were
    /// listening to.</summary>
    public static Size Measure(DiscConfig config, NowPlayingReading reading)
    {
        if (!config.Enabled || string.IsNullOrWhiteSpace(reading.Title)) return Size.Empty;
        int side = Math.Max(config.Size, MinSize);
        return new Size(side, side);
    }

    /// <summary>Everything that turns, composited once. The caller caches this per track and
    /// disposes it when the track changes — see the art cache in NowPlayingSource.</summary>
    public static Bitmap RenderFace(int side, Image? art, Color accent)
    {
        side = Math.Max(side, MinSize);
        var face = new Bitmap(side, side, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

        using var g = Graphics.FromImage(face);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        float r = side / 2f;
        float cx = r, cy = r;

        using (var path = new GraphicsPath())
        {
            path.AddEllipse(0, 0, side - 1, side - 1);
            using var body = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(255, 58, 63, 74),
                SurroundColors = [Color.FromArgb(255, 13, 15, 20)],
                CenterPoint = new PointF(cx - r * 0.3f, cy - r * 0.35f),
            };
            g.FillPath(body, path);
        }

        // Grooves. Faint enough to read as texture rather than as rings.
        using (var groove = new Pen(Color.FromArgb(14, 255, 255, 255), 1f))
            for (float rr = r * 0.42f; rr < r * 0.96f; rr += Math.Max(2.5f, r * 0.07f))
                g.DrawEllipse(groove, cx - rr, cy - rr, rr * 2, rr * 2);

        // One highlight band, so the rotation is visible even when the label is a flat colour.
        using (var clip = new GraphicsPath())
        {
            clip.AddEllipse(0, 0, side - 1, side - 1);
            var saved = g.Save();
            g.SetClip(clip);
            using (var sweep = new LinearGradientBrush(new PointF(0, 0), new PointF(side, side),
                                                       Color.FromArgb(0, 255, 255, 255),
                                                       Color.FromArgb(0, 255, 255, 255)))
            {
                sweep.InterpolationColors = new ColorBlend
                {
                    Colors =
                    [
                        Color.FromArgb(0, 255, 255, 255),
                        Color.FromArgb(26, 255, 255, 255),
                        Color.FromArgb(26, 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255),
                    ],
                    Positions = [0f, 0.47f, 0.53f, 1f],
                };
                g.FillEllipse(sweep, 0, 0, side - 1, side - 1);
            }
            g.Restore(saved);
        }

        // The label: album art if the track has any, otherwise a flat accent disc. Never an empty
        // hole and never a broken-image box.
        float lr = r * LabelRatio;
        using (var labelClip = new GraphicsPath())
        {
            labelClip.AddEllipse(cx - lr, cy - lr, lr * 2, lr * 2);
            var saved = g.Save();
            g.SetClip(labelClip);
            if (art is not null)
                g.DrawImage(art, cx - lr, cy - lr, lr * 2, lr * 2);
            else
                using (var flat = new SolidBrush(accent))
                    g.FillEllipse(flat, cx - lr, cy - lr, lr * 2, lr * 2);
            g.Restore(saved);
        }

        using (var labelEdge = new Pen(Color.FromArgb(115, 0, 0, 0), Math.Max(side * 0.012f, 1f)))
            g.DrawEllipse(labelEdge, cx - lr, cy - lr, lr * 2, lr * 2);

        float sr = Math.Max(r * SpindleRatio, 1.5f);
        using (var spindle = new SolidBrush(Color.FromArgb(235, 10, 12, 18)))
            g.FillEllipse(spindle, cx - sr, cy - sr, sr * 2, sr * 2);

        return face;
    }

    /// <summary><paramref name="turns"/> is the rotation in whole turns; the caller advances it
    /// from a timer while playing and holds it still otherwise.
    ///
    /// <paramref name="playing"/> only dims — a paused record keeps its artwork and its progress,
    /// because the track has not gone anywhere.</summary>
    public static void Paint(Graphics g, Rectangle box, DiscConfig config, Bitmap face,
                             float turns, float progress, bool playing)
    {
        if (box.Width <= 0 || box.Height <= 0) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        float side = Math.Min(box.Width, box.Height);
        float cx = box.X + side / 2f, cy = box.Y + side / 2f;

        var saved = g.Save();
        if (!playing)
        {
            // Dim the whole record rather than drawing a "paused" glyph over it. The state reads
            // without a word, which is the point of the shape.
            var dim = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.55f };
            using var attrs = new System.Drawing.Imaging.ImageAttributes();
            attrs.SetColorMatrix(dim);
            g.TranslateTransform(cx, cy);
            g.RotateTransform(turns * 360f);
            g.DrawImage(face, new Rectangle((int)(-side / 2f), (int)(-side / 2f), (int)side, (int)side),
                        0, 0, face.Width, face.Height, GraphicsUnit.Pixel, attrs);
        }
        else
        {
            g.TranslateTransform(cx, cy);
            g.RotateTransform(turns * 360f);
            g.DrawImage(face, -side / 2f, -side / 2f, side, side);
        }
        g.Restore(saved);

        if (!config.ShowProgress) return;

        float stroke = Math.Max(side * 0.045f, 1.5f);
        var rim = new RectangleF(box.X + stroke / 2f, box.Y + stroke / 2f, side - stroke, side - stroke);
        var accent = ClockRenderer.ParseColor(config.AccentColor);
        if (!playing) accent = Color.FromArgb((int)(accent.A * 0.55f), accent);

        using (var track = new Pen(Color.FromArgb(playing ? 34 : 20, 255, 255, 255), stroke))
            g.DrawEllipse(track, rim);

        float sweep = 360f * Math.Clamp(progress, 0f, 1f);
        if (sweep <= 0) return;
        using var arc = new Pen(accent, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(arc, rim, -90f, sweep);
    }
}
