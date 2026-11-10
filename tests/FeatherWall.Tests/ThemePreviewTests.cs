using System.Drawing;
using FeatherWall.Config;
using FeatherWall.Widgets.Themes;

namespace FeatherWall.Tests;

/// <summary>The Style page's tiles. Each is drawn by the theme's own renderers, so these only
/// check that every theme has one and that it actually draws something.</summary>
public class ThemePreviewTests
{
    private static readonly Size Tile = new(240, 135);
    private static readonly Color Violet = Color.FromArgb(255, 0xC0, 0x8B, 0xFF);

    public static TheoryData<WidgetTheme> Themes()
    {
        var data = new TheoryData<WidgetTheme>();
        foreach (var t in Enum.GetValues<WidgetTheme>()) data.Add(t);
        return data;
    }

    private static bool Uniform(Bitmap b)
    {
        var first = b.GetPixel(0, 0);
        for (int x = 0; x < b.Width; x += 3)
            for (int y = 0; y < b.Height; y += 3)
                if (b.GetPixel(x, y) != first) return false;
        return true;
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void EveryTheme_Renders(WidgetTheme theme)
    {
        using var tile = ThemePreview.Render(theme, Tile, Violet, null);
        Assert.Equal(Tile, tile.Size);
    }

    [Fact]
    public void Themes_LookDifferent()
    {
        using var classic = ThemePreview.Render(WidgetTheme.Classic, Tile, Violet, null);
        using var editorial = ThemePreview.Render(WidgetTheme.Editorial, Tile, Violet, null);
        int differing = 0;
        for (int x = 0; x < Tile.Width; x += 2)
            for (int y = 0; y < Tile.Height; y += 2)
                if (classic.GetPixel(x, y) != editorial.GetPixel(x, y)) differing++;
        Assert.True(differing > 100, $"only {differing} sampled pixels differ");
    }

    [Fact]
    public void NullBackdrop_StillDrawsWidgets()
    {
        // No static frame yet — first run, or no wallpaper chosen. The stand-in is a flat colour
        // here so "not uniform" can only mean the widgets were drawn on it.
        using var tile = ThemePreview.Render(WidgetTheme.Editorial, Tile, Violet, null);
        Assert.False(Uniform(tile));
    }
}

public class ThemePreviewFitTests
{
    [Fact]
    public void LargeRecord_ShrinksEverythingUntilTheyStack()
    {
        // Ritvik's own config: a 260 px record under a 150 px clock overlapped it on the tile.
        var clock = new System.Drawing.Size(420, 260);
        var record = new System.Drawing.Size(300, 270);
        float s = ThemePreview.Fit(clock, record, new System.Drawing.Size(60, 60));
        Assert.True((clock.Height + record.Height) * s <= ThemePreview.CanvasSize.Height - ThemePreview.MarginPx * 3,
                    $"scale {s} still overlaps");
    }

    [Fact]
    public void SmallWidgets_KeepTheirPreviewSize() =>
        Assert.Equal(0.8f, ThemePreview.Fit(new(200, 100), new(150, 80), new(40, 40)));
}
