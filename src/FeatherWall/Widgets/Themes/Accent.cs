using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using FeatherWall.Common;

namespace FeatherWall.Widgets.Themes;

/// <summary>The themes' accent colour: one saturated, dominant hue taken from the wallpaper.
///
/// Sampled once per wallpaper, from the static frame the engine already writes, never per frame.
/// The lightness is clamped into a band so accent text stays readable on the baked shadow and
/// beside white text, whatever the wallpaper is.</summary>
public static partial class Accent
{
    /// <summary>The record's v0.2.0 accent, used when a wallpaper has no colour worth taking.</summary>
    public static readonly Color Fallback = Color.FromArgb(255, 0x8F, 0xB4, 0xFF);

    private const int Grid = 64;
    private const int Bins = 24;
    private const int MinPixels = 20;

    private static readonly HashSet<string> Warned = new(StringComparer.Ordinal);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColour();

    /// <summary>Null when no hue carries enough weight: a greyscale, near-black or near-white
    /// wallpaper. The caller falls back rather than inventing a colour.</summary>
    public static Color? Sample(Bitmap image)
    {
        using var small = new Bitmap(Grid, Grid, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            // Point samples, not an average: averaging mixes a small vivid area into the grey around
            // it, and the samurai frame's violet — the colour a person would pick — vanished that way.
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(image, new Rectangle(0, 0, Grid, Grid));
        }

        var weight = new float[Bins];
        var hueSum = new float[Bins];
        var lightSum = new float[Bins];
        var satSum = new float[Bins];
        var count = new int[Bins];
        for (int x = 0; x < Grid; x++)
            for (int y = 0; y < Grid; y++)
            {
                var c = small.GetPixel(x, y);
                float s = c.GetSaturation(), l = c.GetBrightness();
                // Near-black stays out, but a dark navy is a real colour and is lifted below.
                if (s < 0.25f || l < 0.08f || l > 0.92f) continue;
                float hue = c.GetHue();
                int bin = Math.Min((int)(hue / (360f / Bins)), Bins - 1);
                // Squared, so a vivid minority outweighs a large wash of faintly tinted grey.
                float w = s * s;
                weight[bin] += w;
                hueSum[bin] += hue * w;
                lightSum[bin] += l * w;
                satSum[bin] += s * w;
                count[bin]++;
            }

        if (count.Sum() < MinPixels) return null;

        int best = Array.IndexOf(weight, weight.Max());
        float meanHue = hueSum[best] / weight[best];
        float meanSat = Math.Clamp(satSum[best] / weight[best], 0.45f, 0.85f);
        float meanLight = Math.Clamp(lightSum[best] / weight[best], 0.70f, 0.80f);
        return FromHsl(meanHue, meanSat, meanLight);
    }

    /// <summary>"auto" takes the sample, or the fallback without one. A #RRGGBB fixes the colour.
    /// Anything else is treated as "auto" — ParseColor's answer to garbage is white, and white
    /// accent text on white text is the one result this must never produce.</summary>
    public static Color Resolve(string? setting, Color? sampled)
    {
        var value = setting?.Trim() ?? "";
        if (value.Length > 0 && HexColour().IsMatch(value))
            return ColorTranslator.FromHtml(value);

        if (!string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
            lock (Warned)
                if (Warned.Add(value))
                    Log.Warn($"Widget accent '{value}' is not \"auto\" or #RRGGBB, using auto");
        return sampled ?? Fallback;
    }

    /// <summary>Where <see cref="Resolve"/>'s answer came from, in words for the Style page:
    /// "fixed", "from wallpaper", or "default" when nothing has been sampled.</summary>
    public static string Source(string? setting, Color? sampled)
    {
        var value = setting?.Trim() ?? "";
        if (value.Length > 0 && HexColour().IsMatch(value)) return "fixed";
        return sampled is null ? "default" : "from wallpaper";
    }

    private static Color FromHsl(float hue, float s, float l)
    {
        float c = (1 - Math.Abs(2 * l - 1)) * s;
        float h = hue / 60f;
        float x = c * (1 - Math.Abs(h % 2 - 1));
        (float r, float g, float b) = (int)h switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        float m = l - c / 2;
        return Color.FromArgb(255, To255(r + m), To255(g + m), To255(b + m));
    }

    private static int To255(float v) => Math.Clamp((int)Math.Round(v * 255), 0, 255);
}
