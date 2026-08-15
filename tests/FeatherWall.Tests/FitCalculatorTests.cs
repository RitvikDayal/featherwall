using FeatherWall.Config;
using FeatherWall.Rendering;

namespace FeatherWall.Tests;

public class FitCalculatorTests
{
    [Fact]
    public void Stretch_FillsDestinationExactly()
    {
        var r = FitCalculator.Compute(1920, 1080, 2560, 1600, FitMode.Stretch);
        Assert.Equal(new FitRect(0, 0, 2560, 1600), r);
    }

    [Fact]
    public void Fit_Letterboxes_WiderVideo_OnTallerScreen()
    {
        // 16:9 source on a 16:10 screen → full width, bars top/bottom
        var r = FitCalculator.Compute(1920, 1080, 2560, 1600, FitMode.Fit);
        Assert.Equal(0, r.X);
        Assert.Equal(2560, r.Width);
        Assert.Equal(1440, r.Height);
        Assert.Equal((1600 - 1440) / 2, r.Y);
    }

    [Fact]
    public void Fill_Covers_WithNegativeOverflow()
    {
        // 16:9 source covering a 16:10 screen → scaled past the width, cropped left/right? No —
        // height is the binding dimension: scale = 1600/1080, width overflows.
        var r = FitCalculator.Compute(1920, 1080, 2560, 1600, FitMode.Fill);
        Assert.Equal(1600, r.Height);
        Assert.True(r.Width > 2560);
        Assert.True(r.X < 0);
        Assert.Equal(0, r.Y);
    }

    [Fact]
    public void SameAspect_AllModesIdentical()
    {
        foreach (var mode in Enum.GetValues<FitMode>())
        {
            var r = FitCalculator.Compute(1920, 1080, 3840, 2160, mode);
            Assert.Equal(new FitRect(0, 0, 3840, 2160), r);
        }
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-5, 100)]
    public void DegenerateSource_FallsBackToStretch(int srcW, int srcH)
    {
        var r = FitCalculator.Compute(srcW, srcH, 800, 600, FitMode.Fill);
        Assert.Equal(new FitRect(0, 0, 800, 600), r);
    }
}
