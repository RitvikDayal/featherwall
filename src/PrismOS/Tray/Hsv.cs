using System.Drawing;
using System.Globalization;

namespace PrismOS.Tray;

/// <summary>Hue, saturation and value — the space a colour picker works in.
///
/// RGB is what the config stores and what GDI+ draws; HSV is what a person picks in. This
/// converts between them, which is the whole reason a picker has an SV square and a hue slider
/// rather than three number boxes.</summary>
public readonly record struct Hsv(double Hue, double Saturation, double Value)
{
    public static Hsv FromRgb(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double hue = 0;
        if (delta > 1e-9)
        {
            if (max == r) hue = 60 * (((g - b) / delta) % 6);
            else if (max == g) hue = 60 * (((b - r) / delta) + 2);
            else hue = 60 * (((r - g) / delta) + 4);
            if (hue < 0) hue += 360;
        }

        double saturation = max <= 1e-9 ? 0 : delta / max;
        return new Hsv(hue, saturation, max);
    }

    public Color ToRgb()
    {
        double c = Value * Saturation;
        double hp = (Hue % 360 + 360) % 360 / 60.0;
        double x = c * (1 - Math.Abs(hp % 2 - 1));
        double m = Value - c;

        double r, g, b;
        if (hp < 1) { r = c; g = x; b = 0; }
        else if (hp < 2) { r = x; g = c; b = 0; }
        else if (hp < 3) { r = 0; g = c; b = x; }
        else if (hp < 4) { r = 0; g = x; b = c; }
        else if (hp < 5) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromArgb(255, ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static int ToByte(double v) => (int)Math.Clamp(Math.Round(v * 255), 0, 255);
}

/// <summary>Alpha as a fraction and as a byte.
///
/// The fraction is what the slider holds and the byte is what the config stores. Converting
/// between them by casting the fraction first loses everything below 1: 240/255 is 0.94, and
/// (int)0.94 * 255 is zero, not 240. The byte is computed from the fraction in one step and
/// rounded, so a slider position comes back as the alpha it started at.</summary>
public static class AlphaScale
{
    public static double ToFraction(int alpha) => Math.Clamp(alpha, 0, 255) / 255.0;

    public static int ToAlpha(double fraction) => (int)Math.Round(Math.Clamp(fraction, 0, 1) * 255);
}

/// <summary>The hex forms the app stores.
///
/// Two of them, and the difference matters: a widget colour is #AARRGGBB, because it carries an
/// alpha the opacity spinner owns, while the accent is #RRGGBB only, because Accent.Resolve
/// matches it against a six-digit pattern and quietly falls back to the wallpaper's colour for
/// anything else.</summary>
public static class ColorHex
{
    /// <summary>#AARRGGBB, the form every widget colour is written in.</summary>
    public static string ToHex(Color color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>#RRGGBB, the form the accent is written in.</summary>
    public static string ToRgbHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Reads #AARRGGBB or #RRGGBB, with or without the hash. A six-digit value is
    /// opaque, matching ClockRenderer.ParseColor.</summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = Color.White;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string hex = text.Trim().TrimStart('#');
        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
            return false;

        switch (hex.Length)
        {
            case 8:
                color = Color.FromArgb(value);
                return true;
            case 6:
                color = Color.FromArgb(unchecked((int)(0xFF000000 | (uint)value)));
                return true;
            default:
                return false;
        }
    }
}
