using System.Text.Json;
using FeatherWall.Config;

namespace FeatherWall.Tests;

public class ConfigTests
{
    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var config = new AppConfig
        {
            Fit = FitMode.Fit,
            MuteVideo = false,
            Volume = 0.7,
            Clock = new ClockConfig
            {
                Enabled = true,
                Anchor = ClockAnchor.BottomCenter,
                TwentyFourHour = true,
                ShowSeconds = true,
                FontSize = 96,
                Color = "#80FF0000",
            },
            Pause = new PauseConfig { OnFullscreen = false },
        };
        config.Assign(@"\\.\DISPLAY1", @"C:\wallpapers\loop.mp4");

        var json = JsonSerializer.Serialize(config, ConfigJsonContext.Default.AppConfig);
        var back = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig)!;

        Assert.Equal(FitMode.Fit, back.Fit);
        Assert.False(back.MuteVideo);
        Assert.Equal(0.7, back.Volume);
        Assert.Equal(ClockAnchor.BottomCenter, back.Clock.Anchor);
        Assert.True(back.Clock.TwentyFourHour);
        Assert.Equal(96, back.Clock.FontSize);
        Assert.Equal("#80FF0000", back.Clock.Color);
        Assert.False(back.Pause.OnFullscreen);
        Assert.Equal(@"C:\wallpapers\loop.mp4", back.WallpaperFor(@"\\.\DISPLAY1"));
    }

    [Fact]
    public void WallpaperFor_ExactMonitorBeatsWildcard()
    {
        var config = new AppConfig();
        config.Assign("*", @"C:\all.mp4");
        config.Assign(@"\\.\DISPLAY2", @"C:\second.mp4");

        Assert.Equal(@"C:\second.mp4", config.WallpaperFor(@"\\.\DISPLAY2"));
        Assert.Equal(@"C:\all.mp4", config.WallpaperFor(@"\\.\DISPLAY1"));
    }

    [Fact]
    public void Assign_OverwritesExistingEntry()
    {
        var config = new AppConfig();
        config.Assign("*", @"C:\a.mp4");
        config.Assign("*", @"C:\b.mp4");

        Assert.Single(config.Wallpapers);
        Assert.Equal(@"C:\b.mp4", config.WallpaperFor(@"\\.\DISPLAY1"));
    }

    [Fact]
    public void WallpaperFor_EmptyConfig_ReturnsNull() =>
        Assert.Null(new AppConfig().WallpaperFor(@"\\.\DISPLAY1"));

    [Fact]
    public void Defaults_AreSensible()
    {
        var config = new AppConfig();
        Assert.Equal(FitMode.Fill, config.Fit);
        Assert.True(config.MuteVideo);
        Assert.True(config.Clock.Enabled);
        Assert.True(config.Pause.OnFullscreen);
    }

    [Fact]
    public void EnumSerializedAsString()
    {
        var json = JsonSerializer.Serialize(new AppConfig { Fit = FitMode.Stretch }, ConfigJsonContext.Default.AppConfig);
        Assert.Contains("\"Stretch\"", json);
    }
}

/// <summary>The info widget's defaults. An upgrade must render exactly as it did before, so
/// every default here is chosen to produce nothing on screen until someone asks for it.</summary>
public class InfoConfigTests
{
    [Fact]
    public void InfoWidget_IsOffByDefault()
    {
        // An upgrade must not silently add lines to someone's wallpaper.
        Assert.False(new AppConfig().Info.Enabled);
    }

    [Fact]
    public void InfoWidget_DefaultsToNowPlayingThenBattery()
    {
        Assert.Equal(["nowPlaying", "battery"], new AppConfig().Info.Sources);
    }

    [Fact]
    public void ConfigWrittenBeforeTheWidgetExisted_LoadsWithItOff()
    {
        // The upgrade path, asserted rather than assumed: a config.json from v0.1.x has no
        // "info" key at all, and must still start with the widget silent.
        const string old = """
        { "wallpapers": [], "fit": "Fill", "clock": { "enabled": true } }
        """;
        var loaded = JsonSerializer.Deserialize(old, ConfigJsonContext.Default.AppConfig)!;
        Assert.NotNull(loaded.Info);
        Assert.False(loaded.Info.Enabled);
    }

    [Fact]
    public void InfoConfig_SurvivesARoundTrip()
    {
        var config = new AppConfig();
        config.Info.Enabled = true;
        config.Info.Sources = ["battery"];
        config.Info.MaxCharacters = 30;

        var json = JsonSerializer.Serialize(config, ConfigJsonContext.Default.AppConfig);
        var back = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig)!;

        Assert.True(back.Info.Enabled);
        Assert.Equal(["battery"], back.Info.Sources);
        Assert.Equal(30, back.Info.MaxCharacters);
    }
}

/// <summary>The halo's defaults. Ember, and on — unlike the widget it attaches to, which is off
/// by default. That exception is deliberate and recorded in the design.</summary>
public class HaloConfigTests
{
    [Fact]
    public void Halo_DefaultsToEmber()
    {
        var h = new AppConfig().Info.Halo;
        Assert.Equal("#FF4D4D", h.LowColor);
        Assert.Equal("#FF9A3C", h.MidColor);
        Assert.Equal("#FFD166", h.HighColor);
        Assert.Equal("#FFF3B0", h.ChargedColor);
    }

    [Fact]
    public void Halo_DefaultsToAttachedOnTheLeft()
    {
        var h = new AppConfig().Info.Halo;
        Assert.False(h.Detached);
        Assert.Equal(HaloPlacement.Left, h.Placement);
    }

    [Fact]
    public void Halo_DefaultThresholdsAre20And50()
    {
        var h = new AppConfig().Info.Halo;
        Assert.Equal(20, h.LowThreshold);
        Assert.Equal(50, h.MidThreshold);
    }

    [Fact]
    public void ConfigWrittenBeforeTheHaloExisted_LoadsWithEmberDefaults()
    {
        const string old = """
        { "wallpapers": [], "info": { "enabled": true } }
        """;
        var loaded = JsonSerializer.Deserialize(old, ConfigJsonContext.Default.AppConfig)!;
        Assert.NotNull(loaded.Info.Halo);
        Assert.True(loaded.Info.Halo.Enabled);
        Assert.Equal("#FF4D4D", loaded.Info.Halo.LowColor);
    }

    [Fact]
    public void HaloConfig_SurvivesARoundTrip()
    {
        var config = new AppConfig();
        config.Info.Halo.Detached = true;
        config.Info.Halo.Anchor = ClockAnchor.BottomRight;
        config.Info.Halo.Size = 52;

        var json = JsonSerializer.Serialize(config, ConfigJsonContext.Default.AppConfig);
        var back = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig)!;

        Assert.True(back.Info.Halo.Detached);
        Assert.Equal(ClockAnchor.BottomRight, back.Info.Halo.Anchor);
        Assert.Equal(52, back.Info.Halo.Size);
    }
}
