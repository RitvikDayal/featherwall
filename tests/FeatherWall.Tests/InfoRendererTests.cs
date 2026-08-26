using System.Drawing;
using FeatherWall.Config;
using FeatherWall.Widgets;
using Xunit;

namespace FeatherWall.Tests;

/// <summary>Layout of the info widget's line stack. Pure, so none of this needs a GPU — the
/// painting itself is verified by looking at the desktop.</summary>
public class InfoRendererTests
{
    private static InfoConfig Config() => new() { FontSize = 20f };

    [Fact]
    public void NullValues_AreOmitted()
    {
        var m = InfoRenderer.Measure(Config(), [null, "87% on battery", null]);
        Assert.Equal(["87% on battery"], m.Lines);
    }

    [Fact]
    public void AllNull_ProducesNoLinesAndNoSize()
    {
        // The overlay reads this as "remove the visual entirely" rather than painting an
        // empty rectangle over the wallpaper.
        var m = InfoRenderer.Measure(Config(), [null, null]);
        Assert.Empty(m.Lines);
        Assert.Equal(0, m.Total.Height);
    }

    [Fact]
    public void Order_IsPreserved()
    {
        var m = InfoRenderer.Measure(Config(), ["♪ Track", "87% on battery"]);
        Assert.Equal(["♪ Track", "87% on battery"], m.Lines);
    }

    [Fact]
    public void Scale_ShrinksTheFont()
    {
        Assert.True(InfoRenderer.Measure(Config(), ["x"], 0.5f).FontSize <
                    InfoRenderer.Measure(Config(), ["x"], 1f).FontSize);
    }

    [Fact]
    public void MoreLines_AreTaller()
    {
        // Guards the stacking itself: measuring only the widest line would pass every test
        // above and still draw two lines on top of each other.
        var one = InfoRenderer.Measure(Config(), ["♪ Track"]);
        var two = InfoRenderer.Measure(Config(), ["♪ Track", "87% on battery"]);
        Assert.True(two.Total.Height > one.Total.Height);
    }

    [Fact]
    public void WhitespaceOnlyValue_CountsAsNothing()
    {
        var m = InfoRenderer.Measure(Config(), ["   ", "87% on battery"]);
        Assert.Equal(["87% on battery"], m.Lines);
    }
}

/// <summary>Where the halo sits relative to the lines. The halo box and the text box are both
/// reported so the overlay paints from them rather than recomputing the same arithmetic.</summary>
public class HaloLayoutTests
{
    private static InfoConfig Config(HaloPlacement p) =>
        new() { FontSize = 20f, Halo = new HaloConfig { Placement = p, Size = 30 } };

    private static readonly Size Halo = new(30, 30);

    [Fact]
    public void NoHalo_LeavesTheLayoutExactlyAsItWas()
    {
        var m = InfoRenderer.Measure(Config(HaloPlacement.Left), ["62% charging"]);
        Assert.Equal(Rectangle.Empty, m.HaloBox);
        Assert.Equal(m.Total.Width, m.TextBox.Width + m.TextBox.X * 2);
    }

    [Fact]
    public void Left_PutsTheHaloBeforeTheText()
    {
        var m = InfoRenderer.Measure(Config(HaloPlacement.Left), ["62% charging"], 1f, Halo);
        Assert.True(m.HaloBox.Right <= m.TextBox.Left, $"halo right {m.HaloBox.Right} > text left {m.TextBox.Left}");
        Assert.True(m.Total.Width > m.TextBox.Width);
    }

    [Fact]
    public void Right_PutsTheHaloAfterTheText()
    {
        var m = InfoRenderer.Measure(Config(HaloPlacement.Right), ["62% charging"], 1f, Halo);
        Assert.True(m.HaloBox.Left >= m.TextBox.Right, $"halo left {m.HaloBox.Left} < text right {m.TextBox.Right}");
    }

    [Fact]
    public void Above_PutsTheHaloOverTheText()
    {
        var m = InfoRenderer.Measure(Config(HaloPlacement.Above), ["62% charging"], 1f, Halo);
        Assert.True(m.HaloBox.Bottom <= m.TextBox.Top);
        Assert.True(m.Total.Height > m.TextBox.Height);
    }

    [Fact]
    public void Below_PutsTheHaloUnderTheText()
    {
        var m = InfoRenderer.Measure(Config(HaloPlacement.Below), ["62% charging"], 1f, Halo);
        Assert.True(m.HaloBox.Top >= m.TextBox.Bottom);
    }

    [Fact]
    public void HaloWithNoLines_StillProducesAVisual()
    {
        // The text can be switched off entirely and the halo shown on its own.
        var m = InfoRenderer.Measure(Config(HaloPlacement.Left), [null], 1f, Halo);
        Assert.Empty(m.Lines);
        Assert.True(m.Total.Width >= Halo.Width);
        Assert.Equal(Halo.Width, m.HaloBox.Width);
    }

    [Fact]
    public void EverythingAbsent_ProducesNothing()
    {
        var m = InfoRenderer.Measure(Config(HaloPlacement.Left), [null]);
        Assert.Empty(m.Lines);
        Assert.Equal(0, m.Total.Height);
        Assert.Equal(Rectangle.Empty, m.HaloBox);
    }

    [Fact]
    public void TotalAlwaysContainsBothBoxes()
    {
        // Guards every placement at once: a box outside Total is a box clipped off the surface.
        foreach (var p in Enum.GetValues<HaloPlacement>())
        {
            var m = InfoRenderer.Measure(Config(p), ["62% charging", "♪ Track"], 1f, Halo);
            var total = new Rectangle(0, 0, m.Total.Width, m.Total.Height);
            Assert.True(total.Contains(m.HaloBox), $"{p}: halo {m.HaloBox} outside {total}");
            Assert.True(total.Contains(m.TextBox), $"{p}: text {m.TextBox} outside {total}");
        }
    }
}
