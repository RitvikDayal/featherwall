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
