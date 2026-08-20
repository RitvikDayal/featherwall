using System.Drawing;
using FeatherWall.Config;
using FeatherWall.Interop;
using FeatherWall.Widgets;
using Xunit;

namespace FeatherWall.Tests;

/// <summary>Independent date styling, asked for on r/coolgithubprojects by a user two days into
/// daily use. The first three tests are the ones that matter: every default must reproduce the
/// hardcoded pre-2026-08-20 rendering exactly, or the feature is a regression for everyone who
/// never asked for it.</summary>
public class ClockDateStyleTests
{
    private static ClockConfig Default() => new();

    [Fact]
    public void DefaultDateFace_IsSegoeUI_TheOldHardcodedValue()
    {
        Assert.Equal("Segoe UI", ClockRenderer.DateFontFamily(Default()));
    }

    [Fact]
    public void DefaultDateSize_MatchesTheOldSixteenPercentWithAnElevenPixelFloor()
    {
        var config = Default();
        Assert.Equal(110f * 0.16f, ClockRenderer.DateFontSize(config, 110f), 4);
        // Floor bites under a small clock, exactly as Math.Max(fontSize * 0.16f, 11f) did.
        Assert.Equal(11f, ClockRenderer.DateFontSize(config, 40f), 4);
    }

    [Fact]
    public void DefaultDateColour_IsTheTimeColourAtEightyPercentAlpha()
    {
        var time = Color.FromArgb(240, 255, 255, 255);
        var date = ClockRenderer.DateColorFor(Default(), time);

        Assert.Equal((int)(240 * 0.80), date.A);
        Assert.Equal(255, date.R);
        Assert.Equal(255, date.G);
        Assert.Equal(255, date.B);
    }

    [Fact]
    public void EmptyDateFace_InheritsTheTimeFace()
    {
        var config = Default();
        config.FontFamily = "Cascadia Mono";
        config.DateFontFamily = "";
        Assert.Equal("Cascadia Mono", ClockRenderer.DateFontFamily(config));

        config.DateFontFamily = null;
        Assert.Equal("Cascadia Mono", ClockRenderer.DateFontFamily(config));
    }

    [Fact]
    public void ExplicitDateFace_Wins()
    {
        var config = Default();
        config.FontFamily = "Segoe UI Light";
        config.DateFontFamily = "Georgia";
        Assert.Equal("Georgia", ClockRenderer.DateFontFamily(config));
    }

    [Fact]
    public void ExplicitDateColour_BeatsTheInheritedOne_AndIgnoresOpacity()
    {
        var config = Default();
        config.DateColor = "#80FF0000";
        config.DateOpacity = 0.1f;

        var date = ClockRenderer.DateColorFor(config, Color.FromArgb(255, 255, 255, 255));

        Assert.Equal(0x80, date.A);
        Assert.Equal(255, date.R);
        Assert.Equal(0, date.G);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(-1f)]  // clamped
    [InlineData(5f)]   // clamped
    public void DateOpacity_IsClampedIntoRange(float opacity)
    {
        var config = Default();
        config.DateOpacity = opacity;
        var date = ClockRenderer.DateColorFor(config, Color.FromArgb(200, 1, 2, 3));
        Assert.InRange(date.A, 0, 200);
    }

    [Fact]
    public void NegativeDateScale_DoesNotProduceAnInvertedFont()
    {
        var config = Default();
        config.DateFontScale = -2f;
        Assert.True(ClockRenderer.DateFontSize(config, 110f) > 0);
    }

    // ---- per-edge margins -------------------------------------------------------------------

    private static readonly RECT Work = new(0, 0, 1920, 1080);

    [Fact]
    public void PerEdgeMargins_OnlyConsultTheEdgesTheAnchorTouches()
    {
        // BottomRight uses right and bottom; left and top are set absurdly and must not matter.
        var p = ClockLayout.Position(Work, 400, 200, ClockAnchor.BottomRight,
            marginLeft: 9999, marginTop: 9999, marginRight: 30, marginBottom: 40);

        Assert.Equal(1920 - 400 - 30, p.X);
        Assert.Equal(1080 - 200 - 40, p.Y);
    }

    [Fact]
    public void PerEdgeMargins_TopLeftUsesLeftAndTop()
    {
        var p = ClockLayout.Position(Work, 400, 200, ClockAnchor.TopLeft,
            marginLeft: 12, marginTop: 34, marginRight: 9999, marginBottom: 9999);

        Assert.Equal(12, p.X);
        Assert.Equal(34, p.Y);
    }

    [Fact]
    public void CentredAnchor_IgnoresAllFourMargins()
    {
        var p = ClockLayout.Position(Work, 400, 200, ClockAnchor.Center, 111, 222, 333, 444);

        Assert.Equal((1920 - 400) / 2, p.X);
        Assert.Equal((1080 - 200) / 2, p.Y);
    }

    [Fact]
    public void TwoArgOverload_BehavesAsTheFourArgOneWithSymmetricMargins()
    {
        foreach (ClockAnchor anchor in Enum.GetValues<ClockAnchor>())
        {
            var symmetric = ClockLayout.Position(Work, 400, 200, anchor, 48, 96);
            var explicitEdges = ClockLayout.Position(Work, 400, 200, anchor, 48, 96, 48, 96);
            Assert.Equal(symmetric.X, explicitEdges.X);
            Assert.Equal(symmetric.Y, explicitEdges.Y);
        }
    }
}
