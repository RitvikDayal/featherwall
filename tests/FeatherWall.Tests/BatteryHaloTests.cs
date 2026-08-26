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

    // These assert which BAND the ramp picks, by reading the band's own colour out of the config.
    // Pinning literal hex here would mean every palette change broke tests about selection logic.
    private static Color Low(HaloConfig c) => Hex(c.LowColor);
    private static Color Mid(HaloConfig c) => Hex(c.MidColor);
    private static Color High(HaloConfig c) => Hex(c.HighColor);
    private static Color Charged(HaloConfig c) => Hex(c.ChargedColor);

    [Theory]
    [InlineData(1)]
    [InlineData(20)]      // inclusive upper bound
    public void AtOrBelowLowThreshold_UsesLowColor(int pct)
    {
        var c = Config();
        Assert.Equal(Low(c), BatteryHaloRenderer.ColorFor(pct, BatteryState.OnBattery, c));
    }

    [Theory]
    [InlineData(21)]
    [InlineData(50)]      // inclusive upper bound
    public void BetweenThresholds_UsesMidColor(int pct)
    {
        var c = Config();
        Assert.Equal(Mid(c), BatteryHaloRenderer.ColorFor(pct, BatteryState.OnBattery, c));
    }

    [Fact]
    public void AboveMidThreshold_UsesHighColor()
    {
        var c = Config();
        Assert.Equal(High(c), BatteryHaloRenderer.ColorFor(51, BatteryState.OnBattery, c));
    }

    [Fact]
    public void Charged_UsesChargedColorWhateverTheLevel()
    {
        // Charged is a state, not a level. A charged battery reporting 12% is still charged.
        var c = Config();
        Assert.Equal(Charged(c), BatteryHaloRenderer.ColorFor(12, BatteryState.Charged, c));
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
        Assert.Equal(High(c), BatteryHaloRenderer.ColorFor(3, BatteryState.OnBattery, c));
    }

    [Fact]
    public void InvertedThresholds_DoNotThrow()
    {
        // A nonsensical config must not stop the wallpaper starting — the same rule the Sources
        // list already follows. Low simply wins the overlap.
        var c = Config();
        c.LowThreshold = 80;
        c.MidThreshold = 10;
        Assert.Equal(Low(c), BatteryHaloRenderer.ColorFor(50, BatteryState.OnBattery, c));
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

/// <summary>Defaults after the 2026-08-26 change: the percentage moved inside the ring, so the
/// ring grew, and high/charged became green.</summary>
public class HaloDefaultsTests
{
    [Fact]
    public void HighAndCharged_AreGreen()
    {
        var c = new HaloConfig();
        Assert.Equal("#5FD98A", c.HighColor);
        Assert.Equal("#7CE8A4", c.ChargedColor);
    }

    [Fact]
    public void DefaultSize_LeavesRoomForTheNumber()
    {
        // A number inside a 34 px ring is not legible; 100% overflowed it entirely.
        Assert.True(new HaloConfig().Size >= 44);
    }

    [Fact]
    public void EveryLevelAndStatePaints_WithoutThrowing()
    {
        // Covers the fit-by-measuring path: three digits in the smallest ring is where the text
        // used to be clipped to "10c".
        using var bmp = new System.Drawing.Bitmap(80, 80);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        foreach (int size in new[] { 12, 34, 44, 64 })
            foreach (var state in new[] { BatteryState.OnBattery, BatteryState.Charging, BatteryState.Charged, BatteryState.UnknownSource })
                foreach (int pct in new[] { 0, 8, 62, 100 })
                    BatteryHaloRenderer.Paint(g, new System.Drawing.Rectangle(0, 0, size, size),
                        new HaloConfig { Size = size }, new BatteryReading(pct, state));
    }
}

/// <summary>The panel's first preset has to be the shipped default, or a fresh install opens on
/// "Custom" — which reads as though the user had already changed something.</summary>
public class HaloPresetTests
{
    [Fact]
    public void FirstPreset_MatchesTheShippedDefaults()
    {
        var h = new HaloConfig();
        string[] signal = ["#FF4D4D", "#FF9A3C", "#5FD98A", "#7CE8A4", "#24FFFFFF"];
        string[] actual = [h.LowColor, h.MidColor, h.HighColor, h.ChargedColor, h.TrackColor];
        Assert.Equal(signal, actual);
    }
}
