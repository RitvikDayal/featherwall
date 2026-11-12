using FeatherWall.Common;
using FeatherWall.Config;
using Microsoft.Win32;

namespace FeatherWall.Desktop;

/// <summary>The uninstaller's hook (Setup.exe installs through Velopack). Velopack force-stops the
/// running copy and deletes the install folder, but knows nothing of what the app did outside it:
/// it swapped the user's wallpaper (put back on a normal exit), and may have made a Start menu entry
/// and a Run-at-sign-in value. Those two are removed only when they point into this install, so a
/// zip copy elsewhere keeps its own.</summary>
public static class UninstallCleanup
{
    public static void Run(string installDir)
    {
        // Velopack has already force-stopped the app, so its own exit path never ran.
        Try("restore the wallpaper", () => Engine.RestoreSavedWallpaper());
        Try("restore the desktop", DesktopLayerHost.RestoreDesktop);
        Try("remove the Start menu entry", () => { if (RemoveShortcut(installDir)) ForgetShortcut(); });
        Try("remove the sign-in entry", () => RemoveAutostart(installDir));
    }

    /// <summary>Whether a shortcut target or a Run value, quoted or not, names a file inside
    /// <paramref name="installDir"/>.</summary>
    public static bool PointsInto(string? stored, string installDir)
    {
        if (string.IsNullOrWhiteSpace(stored)) return false;
        string path = stored.Trim();
        if (path.StartsWith('"'))
        {
            int close = path.IndexOf('"', 1);
            path = close < 0 ? path[1..] : path[1..close];
        }
        string dir = Path.TrimEndingDirectorySeparator(installDir) + Path.DirectorySeparatorChar;
        return path.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
    }

    public static bool RemoveShortcut(string installDir, string? lnkPath = null)
    {
        lnkPath ??= StartMenuShortcut.DefaultPath;
        if (!PointsInto(StartMenuShortcut.ReadTarget(lnkPath), installDir)) return false;
        File.Delete(lnkPath);
        return true;
    }

    /// <summary>config.json outlives the uninstall. Still saying the shortcut was added, it would
    /// make a reinstall treat the removed shortcut as one the user deleted, and never recreate it.</summary>
    public static void ForgetShortcut(string? configPath = null)
    {
        configPath ??= ConfigStore.ConfigPath;
        if (!File.Exists(configPath)) return;
        var config = ConfigStore.Load(configPath, out bool unreadable, ranBefore: true);
        if (unreadable || !config.StartMenuShortcutAdded) return;
        config.StartMenuShortcutAdded = false;
        ConfigStore.Save(config, configPath);
    }

    public static bool RemoveAutostart(string installDir, string runKey = Autostart.RunKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
        if (key is null || !PointsInto(key.GetValue(Autostart.ValueName) as string, installDir)) return false;
        key.DeleteValue(Autostart.ValueName, throwOnMissingValue: false);
        return true;
    }

    private static void Try(string what, Action step)
    {
        try { step(); }
        catch (Exception ex) { Log.Warn($"Uninstall: could not {what}: {ex.Message}"); }
    }
}
