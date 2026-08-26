using System.Drawing;
using FeatherWall.Config;
using FeatherWall.Widgets;
using Xunit;

namespace FeatherWall.Tests;

/// <summary>The colour ramp. A step rather than a blend, so each band means something specific —
/// an interpolated colour is always slightly different and therefore never says anything.</summary>
public class HaloColorTests
{
    private static HaloConfig Config() => new();
    private static Color Hex(string s) => ClockRenderer.ParseColor(s);

    [Theory]
    [InlineData(1)]
    [InlineData(20)]      // inclusive upper bound
    public void AtOrBelowLowThreshold_UsesLowColor(int pct) =>
        Assert.Equal(Hex("#FF4D4D"), BatteryHaloRenderer.ColorFor(pct, BatteryState.OnBattery, Config()));

    [Theory]
    [InlineData(21)]
    [InlineData(50)]      // inclusive upper bound
    public void BetweenThresholds_UsesMidColor(int pct) =>
        Assert.Equal(Hex("#FF9A3C"), BatteryHaloRenderer.ColorFor(pct, BatteryState.OnBattery, Config()));

    [Fact]
    public void AboveMidThreshold_UsesHighColor() =>
        Assert.Equal(Hex("#FFD166"), BatteryHaloRenderer.ColorFor(51, BatteryState.OnBattery, Config()));

    [Fact]
    public void Charged_UsesChargedColorWhateverTheLevel()
    {
        // Charged is a state, not a level. A charged battery reporting 12% is still charged.
        Assert.Equal(Hex("#FFF3B0"), BatteryHaloRenderer.ColorFor(12, BatteryState.Charged, Config()));
    }

    [Fact]
    public void RampApplies_OnBatteryAsWellAsCharging()
    {
        // The warning colour must not vanish the moment the machine is unplugged.
        var c = Config();
        Assert.Equal(BatteryHaloRenderer.ColorFor(8, BatteryState.Charging, c),
                     BatteryHaloRenderer.ColorFor(8, BatteryState.OnBattery, c));
    }

    [Fact]
    public void ColorByLevelOff_UsesHighColorEverywhere()
    {
        var c = Config();
        c.ColorByLevel = false;
        Assert.Equal(Hex("#FFD166"), BatteryHaloRenderer.ColorFor(3, BatteryState.OnBattery, c));
    }

    [Fact]
    public void InvertedThresholds_DoNotThrow()
    {
        // A nonsensical config must not stop the wallpaper starting — the same rule the Sources
        // list already follows. Low simply wins the overlap.
        var c = Config();
        c.LowThreshold = 80;
        c.MidThreshold = 10;
        Assert.Equal(Hex("#FF4D4D"), BatteryHaloRenderer.ColorFor(50, BatteryState.OnBattery, c));
    }
}

/// <summary>Sizing. Empty means "draw nothing", which the overlay reads as no halo rather than a
/// zero-sized one.</summary>
public class HaloMeasureTests
{
    [Fact]
    public void NoBattery_MeasuresEmpty()
    {
        var m = BatteryHaloRenderer.Measure(new HaloConfig(), new BatteryReading(0, BatteryState.None));
        Assert.Equal(Size.Empty, m);
    }

    [Fact]
    public void Disabled_MeasuresEmpty()
    {
        var c = new HaloConfig { Enabled = false };
        Assert.Equal(Size.Empty, BatteryHaloRenderer.Measure(c, new BatteryReading(50, BatteryState.OnBattery)));
    }

    [Fact]
    public void Enabled_MeasuresSquareAtConfiguredSize()
    {
        var m = BatteryHaloRenderer.Measure(new HaloConfig { Size = 40 }, new BatteryReading(50, BatteryState.OnBattery));
        Assert.Equal(new Size(40, 40), m);
    }

    [Fact]
    public void Scale_GrowsTheRing()
    {
        var c = new HaloConfig { Size = 40 };
        var r = new BatteryReading(50, BatteryState.OnBattery);
        Assert.True(BatteryHaloRenderer.Measure(c, r, 1.5f).Width > BatteryHaloRenderer.Measure(c, r, 1f).Width);
    }

    [Fact]
    public void AbsurdlySmallSize_StillProducesSomethingDrawable()
    {
        // A size of 1 would otherwise produce a ring thinner than a pixel and vanish.
        var m = BatteryHaloRenderer.Measure(new HaloConfig { Size = 1 }, new BatteryReading(50, BatteryState.OnBattery));
        Assert.True(m.Width >= 12);
    }
}
