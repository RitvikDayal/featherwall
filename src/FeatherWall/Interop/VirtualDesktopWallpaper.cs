using Microsoft.Win32;

namespace FeatherWall.Interop;

/// <summary>Windows 11 gives each virtual desktop its own wallpaper, stored under
/// HKCU\...\Explorer\VirtualDesktops\Desktops\{guid}\Wallpaper. During a desktop-switch
/// slide animation, DWM paints the target desktop's static wallpaper before our live layer
/// composites there. Pointing every desktop's wallpaper at our captured frame makes that
/// transition paint a matching frame — no flash. Explorer reads the value when switching to
/// a desktop, so no forced refresh (which would destroy the WorkerW) is needed.</summary>
public static class VirtualDesktopWallpaper
{
    private const string DesktopsKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\Desktops";
    private const string WallpaperValue = "Wallpaper";

    /// <summary>Current per-desktop wallpaper paths, keyed by desktop GUID.</summary>
    public static Dictionary<string, string> ReadAll()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var desktops = Registry.CurrentUser.OpenSubKey(DesktopsKey);
        if (desktops is null) return result;
        foreach (var guid in desktops.GetSubKeyNames())
        {
            using var d = desktops.OpenSubKey(guid);
            if (d?.GetValue(WallpaperValue) is string path)
                result[guid] = path;
        }
        return result;
    }

    /// <summary>Points every virtual desktop's wallpaper at <paramref name="imagePath"/>.</summary>
    public static void SetAll(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)) return;
        using var desktops = Registry.CurrentUser.OpenSubKey(DesktopsKey, writable: true);
        if (desktops is null) return;
        foreach (var guid in desktops.GetSubKeyNames())
        {
            using var d = desktops.OpenSubKey(guid, writable: true);
            d?.SetValue(WallpaperValue, imagePath, RegistryValueKind.String);
        }
    }

    /// <summary>Restores previously-saved per-desktop wallpaper paths.</summary>
    public static void Restore(IReadOnlyDictionary<string, string> saved)
    {
        using var desktops = Registry.CurrentUser.OpenSubKey(DesktopsKey, writable: true);
        if (desktops is null) return;
        foreach (var (guid, path) in saved)
        {
            if (string.IsNullOrEmpty(path)) continue;
            using var d = desktops.OpenSubKey(guid, writable: true);
            d?.SetValue(WallpaperValue, path, RegistryValueKind.String);
        }
    }
}
