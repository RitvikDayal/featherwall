using FeatherWall.Desktop;
using FeatherWall.Interop;
using FeatherWall.Widgets;
using Xunit;

namespace FeatherWall.Tests;

/// <summary>The per-monitor DPI scale, which is deliberately relative to the PRIMARY monitor
/// rather than to 96. That choice is what makes v0.1.0 configs render unchanged, so it is
/// pinned here: if someone "fixes" it to divide by 96, these tests go red.</summary>
public class MonitorDpiTests
{
    private static MonitorInfo Mon(string device, bool primary, uint dpi) =>
        new(device, new RECT(0, 0, 1920, 1080), new RECT(0, 0, 1920, 1040), primary, dpi);

    [Fact]
    public void SingleMonitor_ScalesByExactlyOne_WhateverItsDpi()
    {
        // The whole point: a 150% single-display machine must render identically to v0.1.0.
        var monitors = new List<MonitorInfo> { Mon(@"\\.\DISPLAY1", primary: true, dpi: 144) };
        Assert.Equal(1.0, MonitorTracker.DpiScale(monitors[0], monitors));
    }

    [Fact]
    public void SecondaryAtLowerDpi_ScalesDown_RelativeToPrimary()
    {
        var primary = Mon(@"\\.\DISPLAY1", primary: true, dpi: 144);   // 150%
        var secondary = Mon(@"\\.\DISPLAY2", primary: false, dpi: 96); // 100%
        var monitors = new List<MonitorInfo> { primary, secondary };

        Assert.Equal(1.0, MonitorTracker.DpiScale(primary, monitors));
        Assert.Equal(96.0 / 144.0, MonitorTracker.DpiScale(secondary, monitors), 6);
    }

    [Fact]
    public void SecondaryAtHigherDpi_ScalesUp_RelativeToPrimary()
    {
        var primary = Mon(@"\\.\DISPLAY1", primary: true, dpi: 96);
        var secondary = Mon(@"\\.\DISPLAY2", primary: false, dpi: 192); // 200%
        var monitors = new List<MonitorInfo> { primary, secondary };

        Assert.Equal(2.0, MonitorTracker.DpiScale(secondary, monitors), 6);
    }

    [Fact]
    public void NoPrimaryFlagged_FallsBackToFirstMonitor_NotToNinetySix()
    {
        // Real shape during a display-topology change: the flag can be momentarily absent.
        var a = Mon(@"\\.\DISPLAY1", primary: false, dpi: 144);
        var b = Mon(@"\\.\DISPLAY2", primary: false, dpi: 144);
        var monitors = new List<MonitorInfo> { a, b };

        Assert.Equal(144u, MonitorTracker.PrimaryDpi(monitors));
        Assert.Equal(1.0, MonitorTracker.DpiScale(b, monitors));
    }

    [Fact]
    public void ZeroDpi_DegradesToUnscaled_RatherThanDividingByZero()
    {
        var primary = Mon(@"\\.\DISPLAY1", primary: true, dpi: 0);
        var monitors = new List<MonitorInfo> { primary };
        Assert.Equal(1.0, MonitorTracker.DpiScale(primary, monitors));
    }

    [Fact]
    public void EmptyMonitorList_ReportsDefaultDpi()
    {
        Assert.Equal(Shcore.DefaultDpi, MonitorTracker.PrimaryDpi([]));
    }

    [Theory]
    [InlineData(48, 1.0, 48)]
    [InlineData(48, 1.5, 72)]
    [InlineData(96, 0.6666666666666666, 64)]
    [InlineData(0, 2.0, 0)]
    public void ScaleMargin_RoundsAwayFromZero(int margin, double scale, int expected)
    {
        Assert.Equal(expected, ClockLayout.ScaleMargin(margin, scale));
    }

    [Fact]
    public void ScaleMargin_TreatsNonPositiveScaleAsUnscaled()
    {
        Assert.Equal(48, ClockLayout.ScaleMargin(48, 0));
        Assert.Equal(48, ClockLayout.ScaleMargin(48, -1));
    }

    [Fact]
    public void ScaleMargin_NeverReturnsNegative()
    {
        Assert.Equal(0, ClockLayout.ScaleMargin(-10, 1.5));
    }
}
