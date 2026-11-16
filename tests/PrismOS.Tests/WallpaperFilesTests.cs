using PrismOS.Common;
using PrismOS.Desktop;

namespace PrismOS.Tests;

/// <summary>Monitor device names reach the file system as still-image filenames. A device name is
/// not a filename — it can carry backslashes, colons and GUID braces — so the mapping is worth
/// pinning: a stray separator would put the PNG outside the static folder or fail to write it.</summary>
public class WallpaperFilesTests
{
    [Fact]
    public void StaticPath_KeepsThePngInsideTheStaticFolder()
    {
        string path = WallpaperFiles.StaticPath(@"\\.\DISPLAY1");
        Assert.Equal(WallpaperFiles.StaticDir, Path.GetDirectoryName(path));
        Assert.EndsWith(".png", path);
    }

    [Theory]
    [InlineData(@"\\.\DISPLAY1")]
    [InlineData(@"\\?\DISPLAY#DELA123#5&1a2b3c&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}")]
    [InlineData("..\\..\\evil")]
    [InlineData("C:/Users/Ali/Desktop/x")]
    public void Sanitize_LeavesNothingThatCanEscapeTheFolder(string device)
    {
        string name = WallpaperFiles.Sanitize(device);
        Assert.All(name, c => Assert.True(char.IsLetterOrDigit(c) || c == '_'));
        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain("..", name);
    }

    [Fact]
    public void Sanitize_IsStable_ForTheSameDevice()
    {
        Assert.Equal(WallpaperFiles.Sanitize(@"\\.\DISPLAY1"), WallpaperFiles.Sanitize(@"\\.\DISPLAY1"));
        Assert.NotEqual(WallpaperFiles.Sanitize(@"\\.\DISPLAY1"), WallpaperFiles.Sanitize(@"\\.\DISPLAY2"));
    }

    [Fact]
    public void SavedWallpaper_LivesInTheAppDataFolder()
    {
        // Both restore paths (app exit, uninstaller) read this file; if it moves, an upgraded
        // install would leave the user's original wallpaper behind as an orphan.
        Assert.Equal(Log.Directory, Path.GetDirectoryName(WallpaperFiles.SavedWallpaper));
        Assert.Equal(Log.Directory, Path.GetDirectoryName(WallpaperFiles.SavedDesktopWallpapers));
    }
}
