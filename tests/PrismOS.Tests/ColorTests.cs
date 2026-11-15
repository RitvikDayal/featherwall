using System.Drawing;
using PrismOS.Tray;

namespace PrismOS.Tests;

/// <summary>The colour maths behind the picker. A picker that loses a bit of a colour on the way
/// through HSV makes every colour somebody picks slightly wrong, and nobody would notice.</summary>
public class HsvTests
{
    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    [InlineData(255, 255, 255)]
    [InlineData(0, 0, 0)]
    [InlineData(143, 180, 255)]
    [InlineData(255, 128, 0)]
    public void RoundTrip_KeepsTheColour(int r, int g, int b)
    {
        var colour = Color.FromArgb(255, r, g, b);
        var back = Hsv.FromRgb(colour).ToRgb();

        Assert.InRange(back.R, r - 1, r + 1);
        Assert.InRange(back.G, g - 1, g + 1);
        Assert.InRange(back.B, b - 1, b + 1);
    }

    [Fact]
    public void Grey_HasNoSaturation()
    {
        var hsv = Hsv.FromRgb(Color.FromArgb(255, 128, 128, 128));

        Assert.Equal(0, hsv.Saturation, 3);
    }

    [Fact]
    public void BlackHasNoHue()
    {
        // A hue of zero is red; a black or grey colour has no hue to speak of, and the picker
        // shows a hue slider for it anyway.
        var hsv = Hsv.FromRgb(Color.FromArgb(255, 0, 0, 0));

        Assert.Equal(0, hsv.Saturation, 3);
        Assert.Equal(0, hsv.Value, 3);
    }

    [Fact]
    public void TheHueOfRedIsZero()
    {
        Assert.Equal(0, Hsv.FromRgb(Color.FromArgb(255, 255, 0, 0)).Hue, 1);
    }
}

/// <summary>Alpha as a fraction and as a byte. The conversion is where a colour picker loses its
/// transparency: casting the fraction before multiplying turns 0.94 into zero.</summary>
public class AlphaScaleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(240)]
    [InlineData(255)]
    public void AByteComesBackAsItself(int alpha)
    {
        Assert.Equal(alpha, AlphaScale.ToAlpha(AlphaScale.ToFraction(alpha)));
    }

    [Fact]
    public void TheFractionIsNotTruncatedBeforeMultiplying()
    {
        // (int)(240/255.0) * 255 is 0. This is the bug that made every picked colour fully
        // transparent.
        Assert.Equal(240, AlphaScale.ToAlpha(240 / 255.0));
    }

    [Fact]
    public void OutOfRangeValuesAreClamped()
    {
        Assert.Equal(0, AlphaScale.ToAlpha(-1));
        Assert.Equal(255, AlphaScale.ToAlpha(2));
        Assert.Equal(0.0, AlphaScale.ToFraction(-5), 3);
        Assert.Equal(1.0, AlphaScale.ToFraction(999), 3);
    }
}

/// <summary>The two hex forms the app stores, and which means what.</summary>
public class ColorHexTests
{
    [Fact]
    public void WidgetColours_AreAlphaFirst()
    {
        Assert.Equal("#80FF0000", ColorHex.ToHex(Color.FromArgb(0x80, 0xFF, 0x00, 0x00)));
    }

    [Fact]
    public void TheAccentForm_CarriesNoAlpha()
    {
        // Accent.Resolve matches the accent against a six-digit pattern and falls back to the
        // wallpaper's colour for anything else, so this form is the only one it accepts.
        Assert.Equal("#FF0000", ColorHex.ToRgbHex(Color.FromArgb(0x80, 0xFF, 0x00, 0x00)));
    }

    [Theory]
    [InlineData("#80FF0000", 0x80, 0xFF, 0x00, 0x00)]
    [InlineData("80FF0000", 0x80, 0xFF, 0x00, 0x00)]
    [InlineData("#FF0000", 0xFF, 0xFF, 0x00, 0x00)]
    [InlineData("FF0000", 0xFF, 0xFF, 0x00, 0x00)]
    public void TryParse_ReadsBothForms(string text, int a, int r, int g, int b)
    {
        Assert.True(ColorHex.TryParse(text, out var colour));

        Assert.Equal(a, colour.A);
        Assert.Equal(r, colour.R);
        Assert.Equal(g, colour.G);
        Assert.Equal(b, colour.B);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#GGGGGG")]
    [InlineData("#FFF")]
    [InlineData(null)]
    public void TryParse_RejectsWhatItCannotRead(string? text)
    {
        Assert.False(ColorHex.TryParse(text, out _));
    }

    [Fact]
    public void TheTwoFormsRoundTrip()
    {
        var colour = Color.FromArgb(0x40, 0x12, 0x34, 0x56);

        Assert.True(ColorHex.TryParse(ColorHex.ToHex(colour), out var back));
        Assert.Equal(colour, back);
    }
}
