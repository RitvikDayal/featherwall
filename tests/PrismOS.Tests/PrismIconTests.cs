using System.Drawing;
using PrismOS.Config;
using PrismOS.Tray;

namespace PrismOS.Tests;

/// <summary>The mark. The tray icon and the application icon are the same drawing, so these
/// assert the drawing itself rather than a file.</summary>
public class PrismIconTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    public void TheMarkIsDrawnAtEverySize(int size)
    {
        using var bitmap = PrismIcon.ToBitmap(size);

        var centre = bitmap.GetPixel(size / 2, size / 2);
        Assert.True(centre.A > 200, $"the centre of a {size}px mark should be solid");
    }

    [Fact]
    public void TheCornersAreTransparent()
    {
        using var bitmap = PrismIcon.ToBitmap(64);

        // A square of colour behind the mark would show as a box in the notification area.
        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        Assert.Equal(0, bitmap.GetPixel(63, 63).A);
    }

    [Fact]
    public void TheMarkIsBlue()
    {
        using var bitmap = PrismIcon.ToBitmap(64);

        var centre = bitmap.GetPixel(32, 32);
        Assert.True(centre.B > centre.R, "the mark should read as blue");
    }
}

/// <summary>The application's .ico. Windows picks the size it needs from the file, so a file with
/// one size in it looks wrong everywhere except at that size.</summary>
public class ApplicationIconFileTests
{
    private static string? RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "PrismOS", "PrismOS.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void TheIconFileExists()
    {
        string root = RepositoryRoot()!;
        Assert.NotNull(root);
        Assert.True(File.Exists(Path.Combine(root, "src", "PrismOS", "prismos.ico")));
    }

    [Fact]
    public void TheIconFileHoldsEverySizeWindowsAsksFor()
    {
        string root = RepositoryRoot()!;
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, "src", "PrismOS", "prismos.ico"));

        Assert.Equal(0, bytes[0]);                                  // reserved
        Assert.Equal(0, bytes[1]);
        Assert.Equal(1, bytes[2]);                                  // 1 = icon
        Assert.Equal(0, bytes[3]);
        int count = bytes[4] | (bytes[5] << 8);
        Assert.True(count >= 8, $"expected the eight shell sizes, found {count}");

        var sizes = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            int offset = 6 + i * 16;
            // 0 in a directory entry means 256.
            int size = bytes[offset] == 0 ? 256 : bytes[offset];
            sizes.Add(size);
        }

        foreach (int expected in new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 })
            Assert.Contains(expected, sizes);
    }
}

/// <summary>Resetting. What it must keep is the point: a reset that took the wallpaper with it
/// would be a reset nobody would use twice.</summary>
public class ResetToDefaultsTests
{
    [Fact]
    public void TheWallpapersSurvive()
    {
        var config = new AppConfig();
        config.Assign("*", @"C:\wallpapers\loop.mp4");
        config.Assign(@"\\.\DISPLAY2", @"C:\wallpapers\second.jpg");

        config.ResetToDefaults();

        Assert.Equal(@"C:\wallpapers\loop.mp4", config.WallpaperFor(@"\\.\DISPLAY1"));
        Assert.Equal(@"C:\wallpapers\second.jpg", config.WallpaperFor(@"\\.\DISPLAY2"));
    }

    [Fact]
    public void TheSettingsGoBackToTheirDefaults()
    {
        var config = new AppConfig();
        config.Clock.FontSize = 42;
        config.Clock.TwentyFourHour = true;
        config.Info.Enabled = true;
        config.Widgets.Theme = "tiles";
        config.Fit = FitMode.Stretch;
        config.MuteVideo = false;
        config.Volume = 0.9;
        config.Pause.OnFullscreen = false;

        config.ResetToDefaults();

        Assert.Equal(110f, config.Clock.FontSize);
        Assert.False(config.Clock.TwentyFourHour);
        Assert.False(config.Info.Enabled);
        Assert.Equal("classic", config.Widgets.Theme);
        Assert.Equal(FitMode.Fill, config.Fit);
        Assert.True(config.MuteVideo);
        Assert.Equal(0.3, config.Volume, 3);
        Assert.True(config.Pause.OnFullscreen);
    }

    [Fact]
    public void TheStylesNoticeStaysDismissed()
    {
        // Otherwise a reset would pop the notice up again for someone who already closed it.
        var config = new AppConfig();
        config.Widgets.ThemesNoticeShown = true;

        config.ResetToDefaults();

        Assert.True(config.Widgets.ThemesNoticeShown);
    }

    [Fact]
    public void TheStartMenuFlagStays()
    {
        // Otherwise the app would recreate an entry the user had deleted.
        var config = new AppConfig { StartMenuShortcutAdded = true };

        config.ResetToDefaults();

        Assert.True(config.StartMenuShortcutAdded);
    }

    [Fact]
    public void AResetConfigIsUsable()
    {
        var config = new AppConfig();
        config.ResetToDefaults();

        var json = System.Text.Json.JsonSerializer.Serialize(config, ConfigJsonContext.Default.AppConfig);
        var back = System.Text.Json.JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig)!;

        Assert.NotNull(back.Clock);
        Assert.NotNull(back.Info);
        Assert.NotNull(back.Widgets);
    }
}
