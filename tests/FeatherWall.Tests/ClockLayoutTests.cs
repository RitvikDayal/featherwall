using System.Globalization;
using FeatherWall.Config;
using FeatherWall.Interop;
using FeatherWall.Widgets;

namespace FeatherWall.Tests;

public class ClockLayoutTests
{
    private static readonly RECT Work = new(0, 0, 2560, 1560); // work area (taskbar excluded)

    [Theory]
    [InlineData(ClockAnchor.TopLeft, 48, 48)]
    [InlineData(ClockAnchor.TopRight, 2560 - 400 - 48, 48)]
    [InlineData(ClockAnchor.BottomLeft, 48, 1560 - 200 - 48)]
    [InlineData(ClockAnchor.BottomRight, 2560 - 400 - 48, 1560 - 200 - 48)]
    [InlineData(ClockAnchor.Center, (2560 - 400) / 2, (1560 - 200) / 2)]
    [InlineData(ClockAnchor.TopCenter, (2560 - 400) / 2, 48)]
    [InlineData(ClockAnchor.CenterRight, 2560 - 400 - 48, (1560 - 200) / 2)]
    public void Position_AnchorsCorrectly(ClockAnchor anchor, int expectedX, int expectedY)
    {
        var p = ClockLayout.Position(Work, 400, 200, anchor, 48, 48);
        Assert.Equal(expectedX, p.X);
        Assert.Equal(expectedY, p.Y);
    }

    [Fact]
    public void Position_RespectsMonitorOrigin()
    {
        // Secondary monitor to the left of primary → negative virtual coords
        var work = new RECT(-1920, 200, 0, 1280);
        var p = ClockLayout.Position(work, 300, 100, ClockAnchor.TopLeft, 10, 20);
        Assert.Equal(-1910, p.X);
        Assert.Equal(220, p.Y);
    }

    [Fact]
    public void TimeText_Formats24hWithSeconds()
    {
        var t = new DateTime(2026, 7, 19, 21, 5, 9);
        var old = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            Assert.Equal("21:05:09", ClockLayout.TimeText(t, twentyFourHour: true, showSeconds: true));
            Assert.Equal("21:05", ClockLayout.TimeText(t, twentyFourHour: true, showSeconds: false));
            Assert.Equal("9:05 PM", ClockLayout.TimeText(t, twentyFourHour: false, showSeconds: false));
            Assert.Equal("Sunday, July 19", ClockLayout.DateText(t));
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }

    [Fact]
    public void NextTick_AlignsToSecondBoundary()
    {
        var now = new DateTime(2026, 7, 19, 12, 0, 0, 250);
        Assert.Equal(750, ClockLayout.MillisecondsToNextTick(now, showSeconds: true));
    }

    [Fact]
    public void NextTick_AlignsToMinuteBoundary()
    {
        var now = new DateTime(2026, 7, 19, 12, 0, 30, 500);
        Assert.Equal(29_500, ClockLayout.MillisecondsToNextTick(now, showSeconds: false));
    }

    [Fact]
    public void NextTick_NeverReturnsZeroOrNegative()
    {
        var now = new DateTime(2026, 7, 19, 12, 0, 59, 999);
        Assert.True(ClockLayout.MillisecondsToNextTick(now, showSeconds: true) >= 15);
        Assert.True(ClockLayout.MillisecondsToNextTick(now, showSeconds: false) >= 15);
    }
}
