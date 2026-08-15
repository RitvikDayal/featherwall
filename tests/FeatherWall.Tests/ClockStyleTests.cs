using System.Globalization;
using FeatherWall.Config;
using FeatherWall.Interop;
using FeatherWall.Widgets;

namespace FeatherWall.Tests;

/// <summary>Covers the Mond-style clock config defaults and the color/opacity round-trip
/// the settings panel relies on.</summary>
public class ClockStyleTests
{
    [Fact]
    public void Defaults_MatchMondStyle()
    {
        var c = new ClockConfig();
        Assert.Equal(ClockAnchor.TopCenter, c.Anchor);
        Assert.True(c.Separator);
        Assert.True(c.ShowDate);
        Assert.Equal("Segoe UI Light", c.FontFamily);
        Assert.True(c.FontSize >= 96, "Mond time is large by default");
    }

    [Fact]
    public void ClockConfig_SurvivesJsonRoundTripWithNewFields()
    {
        var c = new ClockConfig { Separator = false, FontFamily = "Inter", Anchor = ClockAnchor.BottomCenter };
        var wrapper = new AppConfig { Clock = c };
        var json = System.Text.Json.JsonSerializer.Serialize(wrapper, ConfigJsonContext.Default.AppConfig);
        var back = System.Text.Json.JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig)!;
        Assert.False(back.Clock.Separator);
        Assert.Equal("Inter", back.Clock.FontFamily);
        Assert.Equal(ClockAnchor.BottomCenter, back.Clock.Anchor);
    }

    [Theory]
    [InlineData("h:mm tt", false, false)]
    [InlineData("HH:mm", true, false)]
    [InlineData("HH:mm:ss", true, true)]
    public void TimeText_MatchesConfiguredFormat(string expectFormat, bool h24, bool seconds)
    {
        var now = new DateTime(2026, 7, 22, 14, 7, 3);
        var expected = now.ToString(expectFormat, CultureInfo.CurrentCulture);
        Assert.Equal(expected, ClockLayout.TimeText(now, h24, seconds));
    }

    [Fact]
    public void Position_TopCenter_IsHorizontallyCentered()
    {
        var work = new RECT(0, 0, 2560, 1560);
        var p = ClockLayout.Position(work, 600, 200, ClockAnchor.TopCenter, 48, 96);
        Assert.Equal((2560 - 600) / 2, p.X);
        Assert.Equal(96, p.Y);
    }
}
