using PrismOS.Desktop;
using PrismOS.Interop;
using PrismOS.Tray;

namespace PrismOS.Tests;

/// <summary>The Wallpaper page's display list. A label that lies about which display it points at
/// puts a wallpaper on the wrong screen, so the mapping is asserted rather than assumed.</summary>
public class WallpaperTargetTests
{
    private static MonitorInfo Monitor(string device, bool primary, int width = 1920, int height = 1080) =>
        new(device, new RECT(0, 0, width, height), new RECT(0, 0, width, height), primary, 96);

    [Fact]
    public void AllDisplaysIsFirst_AndPointsAtTheWildcard()
    {
        var targets = WallpaperTargets.Build([Monitor(@"\\.\DISPLAY1", primary: true)]);

        Assert.Equal("*", targets[0].Device);
        Assert.Equal("All displays", targets[0].Label);
    }

    [Fact]
    public void EachDisplayGetsItsOwnEntry()
    {
        var targets = WallpaperTargets.Build([
            Monitor(@"\\.\DISPLAY1", primary: true),
            Monitor(@"\\.\DISPLAY2", primary: false, 2560, 1600),
        ]);

        Assert.Equal(3, targets.Count);
        Assert.Equal(@"\\.\DISPLAY1", targets[1].Device);
        Assert.Equal(@"\\.\DISPLAY2", targets[2].Device);
    }

    [Fact]
    public void ThePrimaryDisplayIsNamedAsSuch()
    {
        var targets = WallpaperTargets.Build([
            Monitor(@"\\.\DISPLAY1", primary: true),
            Monitor(@"\\.\DISPLAY2", primary: false),
        ]);

        Assert.Contains("Primary display", targets[1].Label);
        Assert.DoesNotContain("Primary display", targets[2].Label);
    }

    [Fact]
    public void TheLabelCarriesTheResolution()
    {
        var targets = WallpaperTargets.Build([Monitor(@"\\.\DISPLAY2", primary: false, 2560, 1600)]);

        Assert.Contains("2560×1600", targets[1].Label);
        Assert.Contains("DISPLAY2", targets[1].Label);
    }

    [Fact]
    public void NoDisplays_StillOffersAllDisplays()
    {
        var targets = WallpaperTargets.Build([]);

        Assert.Single(targets);
        Assert.Equal("*", targets[0].Device);
    }

    [Fact]
    public void Describe_NamesTheWildcardAsEveryDisplay()
    {
        Assert.Equal("all displays", WallpaperTargets.Describe("*"));
        Assert.Equal("DISPLAY1", WallpaperTargets.Describe(@"\\.\DISPLAY1"));
    }
}
