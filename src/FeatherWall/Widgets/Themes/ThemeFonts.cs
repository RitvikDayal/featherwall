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

    // Bahnschrift ships with Windows 10 1809 and later; Orbit and Poster fall back to Segoe UI.
    public static readonly string[] BahnLight = ["Bahnschrift Light", "Segoe UI Light", "Segoe UI"];
    public static readonly string[] Bahn = ["Bahnschrift", "Segoe UI"];
    public static readonly string[] BahnSemibold = ["Bahnschrift SemiBold", "Segoe UI Semibold", "Segoe UI"];

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

    /// <summary>Text the theme faces can draw: Latin, its accents, and common punctuation. Track
    /// titles and artists are anything at all — a Devanagari artist came out as empty boxes.</summary>
    public static bool IsLatin(string text)
    {
        foreach (char c in text)
            if (c > (char)0x024F && c is < (char)0x2000 or > (char)0x206F) return false;
        return true;
    }

    /// <summary>A Windows face that has glyphs for the text's script. GDI+ does not substitute fonts
    /// for missing glyphs the way the shell does, so Segoe UI alone drew a Devanagari artist as
    /// empty boxes. The first non-Latin character decides; Cyrillic, Greek, Arabic and Hebrew are
    /// in Segoe UI itself.</summary>
    public static string ScriptFace(string text)
    {
        foreach (char c in text)
        {
            if (c <= (char)0x024F) continue;
            if (c is >= (char)0x0900 and <= (char)0x0DFF) return "Nirmala UI";      // Indic scripts
            if (c is >= (char)0x0E00 and <= (char)0x0EFF) return "Leelawadee UI";   // Thai, Lao
            if (c is >= (char)0x3040 and <= (char)0x30FF) return "Yu Gothic UI";    // kana
            if (c is >= (char)0x1100 and <= (char)0x11FF or >= (char)0xAC00 and <= (char)0xD7AF) return "Malgun Gothic";
            if (c is >= (char)0x3400 and <= (char)0x9FFF) return "Microsoft YaHei UI"; // Han
        }
        return "Segoe UI";
    }

    /// <summary>For text the theme face cannot draw, at the same size and style.</summary>
    private static Font Fallback(Font font, string text)
    {
        var face = ScriptFace(text);
        return new Font(IsInstalled(face) ? face : "Segoe UI", font.Size, font.Style, GraphicsUnit.Pixel);
    }

    /// <summary>Letters styled with Unicode's look-alike blocks — "𝐋𝐎𝐅𝐈" in Mathematical Bold, full-width
    /// forms — mapped back to plain ones (NFKC), which every theme face carries. Channel names on
    /// music services use these, and they drew as boxes.</summary>
    public static string Plain(string text) =>
        string.IsNullOrEmpty(text) ? text : text.Normalize(System.Text.NormalizationForm.FormKC);

    /// <summary>GDI+ has no letter-spacing, so tracked text is measured and drawn a glyph at a time.
    /// Negative tracking tightens. Non-Latin text is measured whole in a fallback face instead: a
    /// glyph at a time breaks the joins of scripts like Devanagari.</summary>
    public static SizeF MeasureTracked(Graphics g, string text, Font font, float tracking)
    {
        text = Plain(text);
        if (!IsLatin(text))
        {
            using var fallback = Fallback(font, text);
            return g.MeasureString(text, fallback, PointF.Empty, Tight);
        }
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
        text = Plain(text);
        if (!IsLatin(text))
        {
            using var fallback = Fallback(font, text);
            g.DrawString(text, fallback, brush, x, y, Tight);
            return;
        }
        foreach (char c in text)
        {
            string s = c.ToString();
            g.DrawString(s, font, brush, x, y, Tight);
            x += g.MeasureString(s, font, PointF.Empty, Tight).Width + tracking;
        }
    }
}
