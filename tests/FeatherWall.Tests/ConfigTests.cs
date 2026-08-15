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
