using System.Text.Json;
using FeatherWall.Config;

namespace FeatherWall.Tests;

/// <summary>The widgets section picks the theme. An upgrade must land on Classic, and a theme name
/// this build does not know must never cost the rest of the file.</summary>
public class WidgetsConfigTests
{
    [Fact]
    public void Defaults_AreClassicAndAuto()
    {
        var widgets = new AppConfig().Widgets;
        Assert.Equal("classic", widgets.Theme);
        Assert.Equal("auto", widgets.Accent);
        Assert.Equal(WidgetTheme.Classic, widgets.ResolveTheme());
    }

    [Fact]
    public void V020Config_WithoutWidgets_LoadsClassic()
    {
        const string old = """
        { "wallpapers": [], "info": { "enabled": true } }
        """;
        var loaded = JsonSerializer.Deserialize(old, ConfigJsonContext.Default.AppConfig)!;
        Assert.NotNull(loaded.Widgets);
        Assert.Equal(WidgetTheme.Classic, loaded.Widgets.ResolveTheme());
    }

    [Fact]
    public void UnknownTheme_LoadsAsClassic_AndKeepsTheRestOfTheFile()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("fw-config-").FullName, "config.json");
        File.WriteAllText(path, """{ "fit": "Fit", "widgets": { "theme": "neon" } }""");

        var config = ConfigStore.Load(path, out bool unreadable);

        Assert.False(unreadable);
        Assert.Equal(FitMode.Fit, config.Fit);
        Assert.Equal(WidgetTheme.Classic, config.Widgets.ResolveTheme());
    }

    [Theory]
    [InlineData("editorial")]
    [InlineData("Editorial")]
    [InlineData("EDITORIAL")]
    public void Theme_IsCaseInsensitive(string name) =>
        Assert.Equal(WidgetTheme.Editorial, new WidgetsConfig { Theme = name }.ResolveTheme());

    [Fact]
    public void ConfigName_IsTheSpellingTheFileUses_AndResolvesBack()
    {
        Assert.Equal("classic", WidgetsConfig.ConfigName(WidgetTheme.Classic));
        Assert.Equal("editorial", WidgetsConfig.ConfigName(WidgetTheme.Editorial));
        foreach (var theme in Enum.GetValues<WidgetTheme>())
            Assert.Equal(theme, new WidgetsConfig { Theme = WidgetsConfig.ConfigName(theme) }.ResolveTheme());
    }

    [Fact]
    public void Widgets_SurvivesARoundTrip()
    {
        var config = new AppConfig();
        config.Widgets.Theme = "editorial";
        config.Widgets.Accent = "#C08BFF";

        var json = JsonSerializer.Serialize(config, ConfigJsonContext.Default.AppConfig);
        var back = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig)!;

        Assert.Equal("editorial", back.Widgets.Theme);
        Assert.Equal("#C08BFF", back.Widgets.Accent);
    }
}

public class GroupThemeConfigTests
{
    [Theory]
    [InlineData("glassDock", WidgetTheme.GlassDock)]
    [InlineData("orbit", WidgetTheme.Orbit)]
    [InlineData("poster", WidgetTheme.Poster)]
    public void GroupThemes_Resolve(string name, WidgetTheme expected) =>
        Assert.Equal(expected, new WidgetsConfig { Theme = name }.ResolveTheme());

    [Fact]
    public void DisplayName_ReadsAsWords() =>
        Assert.Equal("Glass dock", WidgetsConfig.DisplayName(WidgetTheme.GlassDock));

    [Fact]
    public void Position_DefaultsToTheThemesOwn() =>
        Assert.Null(new WidgetsConfig().Position);

    [Fact]
    public void FreshInstall_GetsOrbit_WithTheInfoWidgetOn()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("fw-config-").FullName, "config.json");
        var config = ConfigStore.Load(path, out _);
        Assert.Equal(WidgetTheme.Orbit, config.Widgets.ResolveTheme());
        Assert.True(config.Info.Enabled);
    }

    [Fact]
    public void Upgrade_KeepsClassic()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("fw-config-").FullName, "config.json");
        File.WriteAllText(path, """{ "wallpapers": [], "info": { "enabled": false } }""");
        var config = ConfigStore.Load(path, out _);
        Assert.Equal(WidgetTheme.Classic, config.Widgets.ResolveTheme());
        Assert.False(config.Info.Enabled);
    }
}

public class WidgetSizeTests
{
    [Fact]
    public void Unset_FollowsTheClocksFontSize() =>
        Assert.Equal(150f / 110f, new WidgetsConfig().ScaleFor(new ClockConfig { FontSize = 150 }), 3);

    [Fact]
    public void Set_IsAPercentage_AndIgnoresTheFontSize() =>
        Assert.Equal(2f, new WidgetsConfig { Size = 200 }.ScaleFor(new ClockConfig { FontSize = 150 }), 3);

    [Theory]
    [InlineData(5, 0.5f)]
    [InlineData(900, 3f)]
    public void Set_IsClampedToWhatFitsAScreen(int size, float expected) =>
        Assert.Equal(expected, new WidgetsConfig { Size = size }.ScaleFor(new ClockConfig()), 3);
}

public class UpgradeDetectionTests
{
    [Fact]
    public void NoConfig_ButRanBefore_IsAnUpgrade_AndKeepsClassic()
    {
        // v0.2.0 wrote config.json only when something was changed, so someone who only ever ran
        // it with the default clock has no file — and is still upgrading (review, Important #2).
        var path = Path.Combine(Directory.CreateTempSubdirectory("fw-config-").FullName, "config.json");
        var config = ConfigStore.Load(path, out _, ranBefore: true);
        Assert.False(config.IsFreshInstall);
        Assert.Equal(WidgetTheme.Classic, config.Widgets.ResolveTheme());
        Assert.False(config.Info.Enabled);
    }
}
