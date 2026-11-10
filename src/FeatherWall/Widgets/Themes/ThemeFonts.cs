using System.Drawing;
using System.Drawing.Text;

namespace FeatherWall.Widgets.Themes;

/// <summary>Font chains for the new themes, resolved against what is actually installed.
///
/// Segoe UI Variable ships with Windows 11 only, so every chain falls back to the Segoe UI weights
/// Windows 10 has. GDI+ also truncates a named instance's family to 31 characters, which is why
/// these are looked up in the installed collection rather than trusted to construct.</summary>
public static class ThemeFonts
{
    public static readonly string[] DisplayLight = ["Segoe UI Variable Display Light", "Segoe UI Light", "Segoe UI"];
    public static readonly string[] TextSemibold = ["Segoe UI Variable Text Semibold", "Segoe UI Semibold", "Segoe UI"];

    private static readonly Lazy<HashSet<string>> Installed = new(() =>
    {
        using var fonts = new InstalledFontCollection();
        return new HashSet<string>(fonts.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
    });

    /// <summary>Replaced by tests to simulate a machine without Segoe UI Variable.</summary>
    public static Func<string, bool> IsInstalled = name => Installed.Value.Contains(name);

    internal static readonly StringFormat Tight = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.MeasureTrailingSpaces,
    };

    public static Font Create(string[] families, float px, FontStyle style = FontStyle.Regular)
    {
        px = Math.Max(px, 1f);
        foreach (var family in families)
            if (IsInstalled(family))
                return new Font(family, px, style, GraphicsUnit.Pixel);
        return new Font(families[^1], px, style, GraphicsUnit.Pixel);
    }

    /// <summary>GDI+ has no letter-spacing, so tracked text is measured and drawn a glyph at a time.
    /// Negative tracking tightens.</summary>
    internal static SizeF MeasureTracked(Graphics g, string text, Font font, float tracking)
    {
        float w = 0, h = 0;
        foreach (char c in text)
        {
            var s = g.MeasureString(c.ToString(), font, PointF.Empty, Tight);
            w += s.Width + tracking;
            h = Math.Max(h, s.Height);
        }
        return new SizeF(Math.Max(w - tracking, 0), h);
    }

    internal static void DrawTracked(Graphics g, string text, Font font, Brush brush, float x, float y, float tracking)
    {
        foreach (char c in text)
        {
            string s = c.ToString();
            g.DrawString(s, font, brush, x, y, Tight);
            x += g.MeasureString(s, font, PointF.Empty, Tight).Width + tracking;
        }
    }
}
