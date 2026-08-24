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
    public readonly record struct InfoMetrics(IReadOnlyList<string> Lines, Size Total, float FontSize);

    private const int LineGap = 4;

    public static Font CreateFont(InfoConfig config, float fontSize)
    {
        try { return new Font(config.FontFamily, fontSize, FontStyle.Regular, GraphicsUnit.Pixel); }
        catch { return new Font(FontFamily.GenericSansSerif, fontSize, FontStyle.Regular, GraphicsUnit.Pixel); }
    }

    /// <summary>Nulls and blanks are dropped rather than rendered. An all-null set produces no
    /// lines and a zero size, which the overlay treats as "remove the visual entirely".</summary>
    public static InfoMetrics Measure(InfoConfig config, IReadOnlyList<string?> values, float scale = 1f)
    {
        float fontSize = Math.Max(config.FontSize * Math.Max(scale, 0f), 6f);
        var lines = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();
        if (lines.Count == 0) return new InfoMetrics(lines, Size.Empty, fontSize);

        using var font = CreateFont(config, fontSize);
        int pad = Padding(config);
        float width = 0, height = 0;
        using (var measure = Graphics.FromHwnd(IntPtr.Zero))
            foreach (var line in lines)
            {
                var size = measure.MeasureString(line, font);
                width = Math.Max(width, size.Width);
                height += size.Height + LineGap;
            }

        var total = new Size(
            Math.Max((int)Math.Ceiling(width) + pad * 2, 1),
            Math.Max((int)Math.Ceiling(height) - LineGap + pad * 2, 1));
        return new InfoMetrics(lines, total, fontSize);
    }

    /// <summary>Room for the shadow offset, so the last line is not clipped by the surface edge.</summary>
    private static int Padding(InfoConfig config) => config.Shadow ? 6 : 2;

    public static void Paint(Graphics g, InfoConfig config, in InfoMetrics metrics, Color color)
    {
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var font = CreateFont(config, metrics.FontSize);
        using var brush = new SolidBrush(color);
        using var shadow = new SolidBrush(Color.FromArgb(color.A / 2, 0, 0, 0));

        int pad = Padding(config);
        float y = pad;
        foreach (var line in metrics.Lines)
        {
            if (config.Shadow) g.DrawString(line, font, shadow, pad + 1.5f, y + 1.5f);
            g.DrawString(line, font, brush, pad, y);
            y += g.MeasureString(line, font).Height + LineGap;
        }
    }
}
