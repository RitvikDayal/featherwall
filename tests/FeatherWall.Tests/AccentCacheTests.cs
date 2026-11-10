using System.Drawing;
using FeatherWall.Widgets.Themes;

namespace FeatherWall.Tests;

/// <summary>When the accent is sampled, and when it is not. The sampler reads a PNG from disk, so
/// every extra call is real work — and a Classic desktop must never pay for one at all.</summary>
public class AccentCacheTests
{
    private const string Dev = @"\\.\DISPLAY1";
    private static readonly Color Violet = Color.FromArgb(255, 0xC4, 0x73, 0xF2);
    private static readonly Color Blue = Color.FromArgb(255, 0x8B, 0xA8, 0xDA);

    private sealed class Sampler
    {
        public int Calls;
        public Func<string, Color?> Answer = _ => Violet;
        public Color? Sample(string png) { Calls++; return Answer(png); }
    }

    [Fact]
    public void Classic_NeverSamples()
    {
        var s = new Sampler();
        var cache = new AccentCache(s.Sample);
        Assert.False(cache.OnStaticFrame(Dev, @"C:\w\a.mp4", @"C:\s\d1.png", themeUsesAccent: false));
        Assert.Equal(0, s.Calls);
    }

    [Fact]
    public void FirstFrame_Samples_AndReportsAChange()
    {
        var s = new Sampler();
        var cache = new AccentCache(s.Sample);
        Assert.True(cache.OnStaticFrame(Dev, @"C:\w\a.mp4", @"C:\s\d1.png", themeUsesAccent: true));
        Assert.Equal(1, s.Calls);
        Assert.Equal(Violet, cache.For(Dev));
    }

    [Fact]
    public void SamePathAgain_DoesNotResample_OrReportAChange()
    {
        // An explorer restart re-applies the same wallpaper and captures a fresh frame. Answering
        // "changed" here would rebuild the widgets on every re-apply.
        var s = new Sampler();
        var cache = new AccentCache(s.Sample);
        cache.OnStaticFrame(Dev, @"C:\w\a.mp4", @"C:\s\d1.png", themeUsesAccent: true);
        Assert.False(cache.OnStaticFrame(Dev, @"C:\w\a.mp4", @"C:\s\d1.png", themeUsesAccent: true));
        Assert.Equal(1, s.Calls);
    }

    [Fact]
    public void NewWallpaper_Resamples()
    {
        var s = new Sampler();
        var cache = new AccentCache(s.Sample);
        cache.OnStaticFrame(Dev, @"C:\w\a.mp4", @"C:\s\d1.png", themeUsesAccent: true);
        s.Answer = _ => Blue;
        Assert.True(cache.OnStaticFrame(Dev, @"C:\w\b.jpg", @"C:\s\d1.png", themeUsesAccent: true));
        Assert.Equal(2, s.Calls);
        Assert.Equal(Blue, cache.For(Dev));
    }

    [Fact]
    public void NoFrameYet_IsNull() =>
        Assert.Null(new AccentCache(new Sampler().Sample).For(Dev));

    [Fact]
    public void SamplerThrows_IsNull_AndNotRetried()
    {
        var s = new Sampler { Answer = _ => throw new IOException("corrupt png") };
        var cache = new AccentCache(s.Sample);
        Assert.False(cache.OnStaticFrame(Dev, @"C:\w\a.mp4", @"C:\s\d1.png", themeUsesAccent: true));
        Assert.Null(cache.For(Dev));
        Assert.Equal(1, s.Calls);
    }

    [Fact]
    public void ThemeSwitchedLater_SamplesOnFirstAsk()
    {
        // Classic when the frame arrived, Editorial chosen afterwards: no new frame is coming until
        // the wallpaper changes, so the remembered one is sampled on demand, once.
        var s = new Sampler();
        var cache = new AccentCache(s.Sample);
        cache.OnStaticFrame(Dev, @"C:\w\a.mp4", @"C:\s\d1.png", themeUsesAccent: false);
        Assert.Equal(Violet, cache.For(Dev));
        Assert.Equal(Violet, cache.For(Dev));
        Assert.Equal(1, s.Calls);
    }
}

/// <summary>Whether a new accent is worth rebuilding the widgets for. Startup delivers one static
/// frame per monitor, and only the monitors that carry a widget can change what is on screen.</summary>
public class StyleRebuildTests
{
    private static readonly WidgetStyle Violet = new(FeatherWall.Config.WidgetTheme.Editorial, Color.FromArgb(255, 196, 115, 242));
    private static readonly WidgetStyle Teal = new(FeatherWall.Config.WidgetTheme.Editorial, Color.FromArgb(255, 144, 205, 213));

    [Fact]
    public void MonitorWithoutWidgets_NeverRebuilds()
    {
        var inUse = new Dictionary<string, WidgetStyle> { [@"\\.\DISPLAY1"] = Violet };
        Assert.False(WidgetStyles.ShouldRebuild(inUse, @"\\.\DISPLAY5", Teal));
    }

    [Fact]
    public void SameStyle_DoesNotRebuild()
    {
        var inUse = new Dictionary<string, WidgetStyle> { [@"\\.\DISPLAY1"] = Violet };
        Assert.False(WidgetStyles.ShouldRebuild(inUse, @"\\.\DISPLAY1", Violet with { }));
    }

    [Fact]
    public void ChangedAccent_OnAWidgetsMonitor_Rebuilds()
    {
        var inUse = new Dictionary<string, WidgetStyle> { [@"\\.\DISPLAY1"] = Violet };
        Assert.True(WidgetStyles.ShouldRebuild(inUse, @"\\.\DISPLAY1", Teal));
    }
}
