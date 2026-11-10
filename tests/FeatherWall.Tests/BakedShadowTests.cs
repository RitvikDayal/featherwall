using System.Drawing;
using System.Drawing.Imaging;
using FeatherWall.Widgets.Themes;

namespace FeatherWall.Tests;

/// <summary>The themes' one shadow, baked into a widget's bitmap at repaint. Asserted on the pixels
/// it produces, because "it looks soft" is the eye check and these are the facts under it.</summary>
public class BakedShadowTests
{
    private static Bitmap Square()
    {
        var b = new Bitmap(60, 60, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(b);
        g.Clear(Color.Transparent);
        g.FillRectangle(Brushes.White, 20, 20, 20, 20);
        return b;
    }

    [Fact]
    public void Shadow_AppearsBelowTheContent()
    {
        using var b = Square();
        Assert.Equal(0, b.GetPixel(30, 42).A);
        BakedShadow.DrawBehind(b, ShadowRecipe.Default, 1f);
        Assert.True(b.GetPixel(30, 42).A > 0);
    }

    [Fact]
    public void Shadow_FallsDownward()
    {
        using var b = Square();
        BakedShadow.DrawBehind(b, ShadowRecipe.Default, 1f);
        Assert.True(b.GetPixel(30, 42).A > b.GetPixel(30, 17).A);
    }

    [Fact]
    public void Content_IsUnchanged()
    {
        using var b = Square();
        BakedShadow.DrawBehind(b, ShadowRecipe.Default, 1f);
        Assert.Equal(Color.White.ToArgb(), b.GetPixel(30, 30).ToArgb());
    }

    [Fact]
    public void Shadow_FadesToZeroWithinPad()
    {
        using var b = Square();
        BakedShadow.DrawBehind(b, ShadowRecipe.Default, 1f);
        Assert.Equal(0, b.GetPixel(0, 0).A);
    }
}
