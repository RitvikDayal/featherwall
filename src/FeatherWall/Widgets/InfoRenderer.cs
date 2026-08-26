using System.Drawing;
using System.Drawing.Text;
using FeatherWall.Config;

namespace FeatherWall.Widgets;

/// <summary>Measures and paints the info widget's stack of lines. Pure for the same reason
/// ClockRenderer is: the layout decisions are testable without a GPU.
///
/// The clock's time/rule/date arrangement is not a list, so sharing that code would be forcing
/// a fit — but colour parsing and anchoring are shared rather than duplicated.</summary>
public static class InfoRenderer
{
    /// <summary><paramref name="TextBox"/> and <paramref name="HaloBox"/> are where each part goes
    /// inside the surface. The overlay paints from them rather than recomputing the arithmetic,
    /// which is how the two can never disagree about the layout.</summary>
    public readonly record struct InfoMetrics(
        IReadOnlyList<string> Lines, Size Total, float FontSize, Rectangle TextBox, Rectangle HaloBox);

    private const int LineGap = 4;

    /// <summary>Space between the halo and the lines. Zero when there are no lines — a gap beside
    /// nothing is just an off-centre halo.</summary>
    private const int HaloGap = 8;

    public static Font CreateFont(InfoConfig config, float fontSize)
    {
        try { return new Font(config.FontFamily, fontSize, FontStyle.Regular, GraphicsUnit.Pixel); }
        catch { return new Font(FontFamily.GenericSansSerif, fontSize, FontStyle.Regular, GraphicsUnit.Pixel); }
    }

    /// <summary>Nulls and blanks are dropped rather than rendered. Nothing to show at all — no
    /// lines and no halo — produces a zero size, which the overlay treats as "remove the visual".
    ///
    /// <paramref name="haloSize"/> defaults to empty, so every caller that does not know about the
    /// halo keeps exactly the layout it had before.</summary>
    public static InfoMetrics Measure(InfoConfig config, IReadOnlyList<string?> values,
                                      float scale = 1f, Size haloSize = default)
    {
        float fontSize = Math.Max(config.FontSize * Math.Max(scale, 0f), 6f);
        var lines = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();
        bool hasHalo = haloSize.Width > 0 && haloSize.Height > 0;

        if (lines.Count == 0 && !hasHalo)
            return new InfoMetrics(lines, Size.Empty, fontSize, Rectangle.Empty, Rectangle.Empty);

        int pad = Padding(config);
        var text = Size.Empty;
        if (lines.Count > 0)
        {
            using var font = CreateFont(config, fontSize);
            float width = 0, height = 0;
            using (var measure = Graphics.FromHwnd(IntPtr.Zero))
                foreach (var line in lines)
                {
                    var size = measure.MeasureString(line, font);
                    width = Math.Max(width, size.Width);
                    height += size.Height + LineGap;
                }
            text = new Size((int)Math.Ceiling(width), (int)Math.Ceiling(height) - LineGap);
        }

        if (!hasHalo)
        {
            var textOnly = new Rectangle(pad, pad, text.Width, text.Height);
            return new InfoMetrics(lines,
                new Size(Math.Max(text.Width + pad * 2, 1), Math.Max(text.Height + pad * 2, 1)),
                fontSize, textOnly, Rectangle.Empty);
        }

        int gap = lines.Count > 0 ? HaloGap : 0;
        int centreText = Math.Max(0, (haloSize.Height - text.Height) / 2);
        int centreHalo = Math.Max(0, (text.Height - haloSize.Height) / 2);
        Rectangle halo, textBox;
        Size total;

        switch (config.Halo.Placement)
        {
            case HaloPlacement.Right:
                textBox = new Rectangle(pad, pad + centreText, text.Width, text.Height);
                halo = new Rectangle(pad + text.Width + gap, pad + centreHalo, haloSize.Width, haloSize.Height);
                total = new Size(text.Width + gap + haloSize.Width + pad * 2,
                                 Math.Max(text.Height, haloSize.Height) + pad * 2);
                break;

            case HaloPlacement.Above:
                halo = new Rectangle(pad, pad, haloSize.Width, haloSize.Height);
                textBox = new Rectangle(pad, pad + haloSize.Height + gap, text.Width, text.Height);
                total = new Size(Math.Max(text.Width, haloSize.Width) + pad * 2,
                                 haloSize.Height + gap + text.Height + pad * 2);
                break;

            case HaloPlacement.Below:
                textBox = new Rectangle(pad, pad, text.Width, text.Height);
                halo = new Rectangle(pad, pad + text.Height + gap, haloSize.Width, haloSize.Height);
                total = new Size(Math.Max(text.Width, haloSize.Width) + pad * 2,
                                 text.Height + gap + haloSize.Height + pad * 2);
                break;

            default: // Left
                halo = new Rectangle(pad, pad + centreHalo, haloSize.Width, haloSize.Height);
                textBox = new Rectangle(pad + haloSize.Width + gap, pad + centreText, text.Width, text.Height);
                total = new Size(haloSize.Width + gap + text.Width + pad * 2,
                                 Math.Max(text.Height, haloSize.Height) + pad * 2);
                break;
        }

        return new InfoMetrics(lines, total, fontSize, textBox, halo);
    }

    /// <summary>Room for the shadow offset, so the last line is not clipped by the surface edge.</summary>
    private static int Padding(InfoConfig config) => config.Shadow ? 6 : 2;

    public static void Paint(Graphics g, InfoConfig config, in InfoMetrics metrics, Color color)
    {
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var font = CreateFont(config, metrics.FontSize);
        using var brush = new SolidBrush(color);
        using var shadow = new SolidBrush(Color.FromArgb(color.A / 2, 0, 0, 0));

        // Painted from the measured box, not from the padding, so the halo's placement moves the
        // text without this method knowing the halo exists.
        float x = metrics.TextBox.X;
        float y = metrics.TextBox.Y;
        foreach (var line in metrics.Lines)
        {
            if (config.Shadow) g.DrawString(line, font, shadow, x + 1.5f, y + 1.5f);
            g.DrawString(line, font, brush, x, y);
            y += g.MeasureString(line, font).Height + LineGap;
        }
    }
}
