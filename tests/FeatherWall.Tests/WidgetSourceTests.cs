using FeatherWall.Interop;
using FeatherWall.Widgets;
using Xunit;

namespace FeatherWall.Tests;

/// <summary>What the battery widget shows, as a pure function of what Windows reports.
/// Null means "show nothing" — a desktop has no battery, and a line reading "N/A" is worse
/// than no line.</summary>
public class BatterySourceTests
{
    private static SYSTEM_POWER_STATUS Status(byte ac, byte flag, byte percent) =>
        new() { ACLineStatus = ac, BatteryFlag = flag, BatteryLifePercent = percent };

    [Fact]
    public void NoSystemBattery_ShowsNothing()
    {
        // BatteryFlag 128 is "no system battery" — every desktop PC.
        Assert.Null(BatterySource.Format(Status(ac: 1, flag: 128, percent: 255)));
    }

    [Fact]
    public void UnknownPercentage_ShowsNothing()
    {
        Assert.Null(BatterySource.Format(Status(ac: 0, flag: 1, percent: 255)));
    }

    [Fact]
    public void OnBattery_ShowsPercentAndState()
    {
        Assert.Equal("87% on battery", BatterySource.Format(Status(ac: 0, flag: 1, percent: 87)));
    }

    [Fact]
    public void Charging_SaysCharging()
    {
        Assert.Equal("87% charging", BatterySource.Format(Status(ac: 1, flag: 8, percent: 87)));
    }

    [Fact]
    public void FullOnAc_SaysCharged()
    {
        Assert.Equal("charged", BatterySource.Format(Status(ac: 1, flag: 1, percent: 100)));
    }
}
