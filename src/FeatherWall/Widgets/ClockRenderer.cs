using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using FeatherWall.Config;

namespace FeatherWall.Widgets;

/// <summary>Measures and draws the Mond-style clock: large light-weight time, hairline rule, small
/// dimmed date. Shared by the desktop overlay and the settings-panel preview so the panel shows
/// exactly what lands on the wallpaper — a preview drawn by separate code is a preview that lies.</summary>
public static class ClockRenderer
{
    public readonly record struct Metrics(
        string Time, string? Date, SizeF TimeSize, SizeF DateSize,
        int Pad, int SeparatorGap, Size Total, float FontSize);

    public static Font CreateTimeFont(ClockConfig config, float fontSize)
    {
        try { return new Font(config.FontFamily, fontSize, FontStyle.Regular, GraphicsUnit.Pixel); }
        catch { return new Font(FontFamily.GenericSansSerif, fontSize, FontStyle.Regular, GraphicsUnit.Pixel); }
    }

    public static Font CreateDateFont(float fontSize) =>
        new("Segoe UI", Math.Max(fontSize * 0.16f, 11f), FontStyle.Regular, GraphicsUnit.Pixel);

    /// <summary><paramref name="scale"/> shrinks the whole widget for the settings preview while
    /// keeping every proportion identical to the desktop rendering.</summary>
    public static Metrics Measure(ClockConfig config, DateTime now, float scale = 1f)
    {
        float fontSize = Math.Max(config.FontSize * scale, 8f);
        string time = ClockLayout.TimeText(now, config.TwentyFourHour, config.ShowSeconds);
        string? date = config.ShowDate ? ClockLayout.DateText(now) : null;

        using var timeFont = CreateTimeFont(config, fontSize);
        using var dateFont = CreateDateFont(fontSize);

        SizeF timeSize, dateSize = SizeF.Empty;
        using (var measure = Graphics.FromHwnd(IntPtr.Zero))
        {
            timeSize = measure.MeasureString(time, timeFont);
            if (date is not null) dateSize = measure.MeasureString(date, dateFont);
        }

        int pad = config.Shadow ? 6 : 2;
        int separatorGap = date is not null && config.Separator ? (int)(fontSize * 0.10f) + 8 : 0;
        var total = new Size(
            Math.Max((int)Math.Ceiling(Math.Max(timeSize.Width, dateSize.Width)) + pad * 2, 1),
            Math.Max((int)Math.Ceiling(timeSize.Height + separatorGap + dateSize.Height) + pad * 2, 1));

        return new Metrics(time, date, timeSize, dateSize, pad, separatorGap, total, fontSize);
    }

    /// <summary>Draws the widget into <paramref name="surface"/>, horizontally centred.</summary>
    public static void Paint(Graphics g, ClockConfig config, in Metrics m, Color color, Size surface)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var timeFont = CreateTimeFont(config, m.FontSize);
        using var dateFont = CreateDateFont(m.FontSize);

        // GDI+ MeasureString pads generously above thin faces; tuck the blocks together.
        float y = m.Pad;
        DrawText(g, config, m.Time, timeFont, (surface.Width - m.TimeSize.Width) / 2f, y, color);
        y += m.TimeSize.Height;

        if (m.Date is null) return;

        if (config.Separator)
        {
            float ruleY = y + m.SeparatorGap / 2f;
            float ruleHalf = Math.Min(Math.Max(m.TimeSize.Width * 0.55f, 120f * (m.FontSize / Math.Max(config.FontSize, 1f))),
                                      surface.Width - m.Pad * 2) / 2f;
            if (config.Shadow)
            {
                using var shadowPen = new Pen(Color.FromArgb(120, 0, 0, 0), 1f);
                g.DrawLine(shadowPen, surface.Width / 2f - ruleHalf + 1, ruleY + 1, surface.Width / 2f + ruleHalf + 1, ruleY + 1);
            }
            using var rulePen = new Pen(Color.FromArgb((int)(color.A * 0.55), color.R, color.G, color.B), 1f);
            g.DrawLine(rulePen, surface.Width / 2f - ruleHalf, ruleY, surface.Width / 2f + ruleHalf, ruleY);
            y += m.SeparatorGap;
        }

        var dateColor = Color.FromArgb((int)(color.A * 0.80), color.R, color.G, color.B);
        DrawText(g, config, m.Date, dateFont, (surface.Width - m.DateSize.Width) / 2f, y, dateColor);
    }

    private static void DrawText(Graphics g, ClockConfig config, string text, Font font, float x, float y, Color color)
    {
        if (config.Shadow)
        {
            using var shadowBrush = new SolidBrush(Color.FromArgb(140, 0, 0, 0));
            g.DrawString(text, font, shadowBrush, x + 2, y + 2);
        }
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, x, y);
    }

    public static Color ParseColor(string hex)
    {
        try
        {
            hex = hex.TrimStart('#');
            return hex.Length switch
            {
                8 => Color.FromArgb(int.Parse(hex, System.Globalization.NumberStyles.HexNumber)),
                6 => Color.FromArgb(unchecked((int)(0xFF000000 | uint.Parse(hex, System.Globalization.NumberStyles.HexNumber)))),
                _ => Color.White,
            };
        }
        catch
        {
            return Color.White;
        }
    }
}
