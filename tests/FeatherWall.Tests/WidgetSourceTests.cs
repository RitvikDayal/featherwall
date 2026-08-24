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

/// <summary>The registration list and the routing must agree. A GUID registered but claimed by
/// nobody is a subscription paid for and dropped — the defect found in the session display
/// state on 2026-08-22, which was registered for months and read nowhere.</summary>
public class PowerSettingRoutingTests
{
    [Fact]
    public void EveryRegisteredSettingIsClaimedBySomeConsumer()
    {
        foreach (var setting in PowerNotifications.RegisteredSettings)
        {
            bool claimed =
                PowerNotifications.IsDisplayState(setting) ||
                setting == PowerNotifications.AcDcPowerSource ||
                setting == PowerNotifications.PowerSavingStatus ||
                setting == PowerNotifications.BatteryPercentageRemaining;
            Assert.True(claimed, $"{setting} is registered but no consumer claims it");
        }
    }

    [Fact]
    public void BatteryPercentageGuid_MatchesTheSdkHeader()
    {
        // GUID_BATTERY_PERCENTAGE_REMAINING, winnt.h. A wrong value registers successfully and
        // then never fires, so nothing else in this suite would notice.
        Assert.Equal(new Guid("a7ad8041-b45a-4cae-87a3-eecbb468a9e1"),
                     PowerNotifications.BatteryPercentageRemaining);
    }

    [Fact]
    public void BatteryPercentage_IsNotTreatedAsADisplayState() =>
        Assert.False(PowerNotifications.IsDisplayState(PowerNotifications.BatteryPercentageRemaining));
}
