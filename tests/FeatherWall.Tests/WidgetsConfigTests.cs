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
