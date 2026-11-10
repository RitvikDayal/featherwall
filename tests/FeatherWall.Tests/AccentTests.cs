using System.Drawing;
using FeatherWall.Widgets.Themes;

namespace FeatherWall.Tests;

/// <summary>The accent is sampled once per wallpaper. It has to come out readable whatever the
/// wallpaper is, and a malformed override must never turn into white.</summary>
public class AccentTests
{
    private static Bitmap Solid(Color c, int side = 64)
    {
        var b = new Bitmap(side, side);
        using var g = Graphics.FromImage(b);
        g.Clear(c);
        return b;
    }

    private static float HueDistance(float a, float b)
    {
        float d = Math.Abs(a - b) % 360f;
        return d > 180f ? 360f - d : d;
    }

    [Fact]
    public void Sample_SolidRed_IsAReadableRed()
    {
        using var image = Solid(Color.FromArgb(0xC0, 0, 0));
        var accent = Accent.Sample(image);
        Assert.NotNull(accent);
        Assert.InRange(HueDistance(accent!.Value.GetHue(), 0f), 0f, 15f);
        Assert.InRange(accent.Value.GetBrightness(), 0.69f, 0.81f);
    }

    [Fact]
    public void Sample_DominantHueWins()
    {
        using var image = new Bitmap(100, 100);
        using (var g = Graphics.FromImage(image))
        {
            g.Clear(ColorTranslator.FromHtml("#2050C0"));
            using var minority = new SolidBrush(ColorTranslator.FromHtml("#C04020"));
            g.FillRectangle(minority, 0, 0, 100, 30);
        }
        var accent = Accent.Sample(image);
        Assert.NotNull(accent);
        Assert.InRange(accent!.Value.GetHue(), 200f, 240f);
    }

    [Fact]
    public void Sample_SparseVividAccent_BeatsTintedGrey()
    {
        // The samurai frame: violet in streaks over a large blue-grey sky. A person picks the
        // violet; averaging the frame down first loses it into the grey.
        using var image = new Bitmap(256, 256);
        using (var g = Graphics.FromImage(image))
        {
            g.Clear(ColorTranslator.FromHtml("#66788F"));
            using var vivid = new SolidBrush(ColorTranslator.FromHtml("#8A3CD0"));
            for (int x = 0; x < 256; x += 8)
                for (int y = 0; y < 256; y += 8)
                    g.FillRectangle(vivid, x, y, 2, 2);
        }
        var accent = Accent.Sample(image);
        Assert.NotNull(accent);
        Assert.InRange(accent!.Value.GetHue(), 260f, 290f);
    }

    [Fact]
    public void Sample_Greyscale_IsNull()
    {
        using var image = new Bitmap(64, 64);
        for (int x = 0; x < 64; x++)
            for (int y = 0; y < 64; y++)
            {
                int v = x * 4;
                image.SetPixel(x, y, Color.FromArgb(v, v, v));
            }
        Assert.Null(Accent.Sample(image));
    }

    [Fact]
    public void Sample_NearBlackNavy_IsLiftedIntoTheBand()
    {
        using var image = Solid(ColorTranslator.FromHtml("#0A1030"));
        var accent = Accent.Sample(image);
        Assert.NotNull(accent);
        Assert.True(accent!.Value.GetBrightness() >= 0.69f);
    }

    [Fact]
    public void Resolve_Auto_UsesSampleThenFallback()
    {
        var red = Color.FromArgb(255, 220, 60, 60);
        Assert.Equal(red, Accent.Resolve("auto", red));
        Assert.Equal(Accent.Fallback, Accent.Resolve("auto", null));
    }

    [Fact]
    public void Resolve_Hex_Overrides()
    {
        var red = Color.FromArgb(255, 220, 60, 60);
        Assert.Equal(Color.FromArgb(255, 0xC0, 0x8B, 0xFF).ToArgb(), Accent.Resolve("#C08BFF", red).ToArgb());
    }

    [Theory]
    [InlineData("#12")]
    [InlineData("blue")]
    [InlineData("")]
    public void Resolve_Garbage_IsAuto_NotWhite(string setting) =>
        Assert.Equal(Accent.Fallback, Accent.Resolve(setting, null));
}
