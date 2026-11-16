using PrismOS.Common;
using PrismOS.Interop;

namespace PrismOS.Desktop;

/// <summary>Puts back the wallpaper that was current before PrismOS started.
///
/// Shared by two callers with different timing: the app's own exit (the user quit, or the system
/// is shutting down) and the uninstaller. Velopack force-stops the app before its uninstall hook
/// runs, so the hook cannot rely on the app's exit path having happened — it reads the saved file
/// itself. Living here rather than on <c>Engine</c> keeps the uninstaller from depending on the
/// shell assembly.</summary>
public static class WallpaperRestore
{
    /// <summary>Restores the saved wallpaper and deletes the saved copy. <paramref name="original"/>
    /// is the path the running app remembered at startup; when null (the uninstaller) the saved
    /// file is read instead.</summary>
    public static void Restore(string? original = null)
    {
        try
        {
            if (File.Exists(WallpaperFiles.SavedDesktopWallpapers))
            {
                var saved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(WallpaperFiles.SavedDesktopWallpapers))
                {
                    int tab = line.IndexOf('\t');
                    if (tab > 0) saved[line[..tab]] = line[(tab + 1)..];
                }
                VirtualDesktopWallpaper.Restore(saved);
                File.Delete(WallpaperFiles.SavedDesktopWallpapers);
            }

            original ??= File.Exists(WallpaperFiles.SavedWallpaper)
                ? File.ReadAllText(WallpaperFiles.SavedWallpaper).Trim()
                : null;
            if (!string.IsNullOrEmpty(original))
                DesktopWallpaper.RestoreCurrent(original);
            if (File.Exists(WallpaperFiles.SavedWallpaper))
                File.Delete(WallpaperFiles.SavedWallpaper);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not restore original wallpaper: {ex.Message}");
        }
    }
}
