using PrismOS.Common;

namespace PrismOS.Desktop;

/// <summary>Where PrismOS keeps the files it has to survive a restart with, under
/// %LOCALAPPDATA%\PrismOS: the stills handed to Windows for the desktop-switch and lock-screen
/// images, and the wallpaper that was there before PrismOS started, so an exit or an uninstall
/// can put it back.</summary>
public static class WallpaperFiles
{
    /// <summary>Captured stills, one PNG per monitor.</summary>
    public static string StaticDir => Path.Combine(Log.Directory, "static");

    /// <summary>The still for one monitor. A device name is not a filename — it can carry
    /// backslashes, colons and GUID braces — so it is flattened to letters and digits.</summary>
    public static string StaticPath(string device) =>
        Path.Combine(StaticDir, Sanitize(device) + ".png");

    /// <summary>The wallpaper that was current before PrismOS first ran.</summary>
    public static string SavedWallpaper => Path.Combine(Log.Directory, "original-wallpaper.txt");

    /// <summary>Windows 11 per-virtual-desktop wallpapers, one "desktop-tab path" per line.</summary>
    public static string SavedDesktopWallpapers =>
        Path.Combine(Log.Directory, "original-vd-wallpapers.tsv");

    /// <summary>Flattens a monitor device name into a filename that cannot escape
    /// <see cref="StaticDir"/>.</summary>
    public static string Sanitize(string device) =>
        new(device.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
}
