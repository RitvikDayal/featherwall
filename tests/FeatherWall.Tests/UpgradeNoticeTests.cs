using FeatherWall.Config;
using FeatherWall.Tray;

namespace FeatherWall.Tests;

/// <summary>The one-time "new widget styles" notice: upgraders on Classic see it once; nobody else
/// ever does, and a config that failed to load is never written to on its account.</summary>
public class UpgradeNoticeTests
{
    private static AppConfig Upgrader() => new();

    [Fact]
    public void Upgrader_OnClassic_SeesItOnce()
    {
        var config = Upgrader();
        Assert.True(UpgradeNotice.ShouldShow(config, configUnreadable: false));
        config.Widgets.ThemesNoticeShown = true;
        Assert.False(UpgradeNotice.ShouldShow(config, configUnreadable: false));
    }

    [Fact]
    public void FreshInstall_NeverSeesIt()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("fw-config-").FullName, "config.json");
        var fresh = ConfigStore.Load(path, out _);
        Assert.True(fresh.IsFreshInstall);
        Assert.False(UpgradeNotice.ShouldShow(fresh, configUnreadable: false));
    }

    [Fact]
    public void SomeoneAlreadyOnANewTheme_DoesNotNeedIt()
    {
        var config = Upgrader();
        config.Widgets.Theme = "editorial";
        Assert.False(UpgradeNotice.ShouldShow(config, configUnreadable: false));
    }

    [Fact]
    public void UnreadableConfig_IsLeftAlone()
    {
        // Showing it means saving ThemesNoticeShown, and saving over a config that failed to load
        // replaces the user's settings with defaults — the bug PR #24 fixed for the Start menu.
        Assert.False(UpgradeNotice.ShouldShow(Upgrader(), configUnreadable: true));
    }

    [Fact]
    public void NoticeFlag_SurvivesARoundTrip_AndFreshIsNotSaved()
    {
        var config = new AppConfig();
        config.Widgets.ThemesNoticeShown = true;
        var json = System.Text.Json.JsonSerializer.Serialize(config, ConfigJsonContext.Default.AppConfig);
        Assert.DoesNotContain("isFreshInstall", json, StringComparison.OrdinalIgnoreCase);
        var back = System.Text.Json.JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig)!;
        Assert.True(back.Widgets.ThemesNoticeShown);
    }
}

/// <summary>Only the balloon that asked for it reacts to a click. Error and download balloons stay
/// inert, so clicking one never opens settings.</summary>
public class BalloonClickTests
{
    [Fact]
    public void Click_RunsTheBalloonsAction_Once()
    {
        int runs = 0;
        var clicks = new BalloonClick();
        clicks.Set(() => runs++);
        clicks.OnClick();
        clicks.OnClick();
        Assert.Equal(1, runs);
    }

    [Fact]
    public void AnotherBalloon_ClearsIt()
    {
        int runs = 0;
        var clicks = new BalloonClick();
        clicks.Set(() => runs++);
        clicks.Set(null);
        clicks.OnClick();
        Assert.Equal(0, runs);
    }
}
