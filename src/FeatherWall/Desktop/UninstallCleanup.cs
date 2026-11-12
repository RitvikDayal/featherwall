using System.Diagnostics;
using FeatherWall.Common;
using FeatherWall.Config;
using FeatherWall.Interop;
using Microsoft.Win32;

namespace FeatherWall.Desktop;

/// <summary>The uninstaller's hook (Setup.exe installs through Velopack). Velopack force-stops the
/// running copy and deletes the install folder, but knows nothing of what the app did outside it:
/// it swapped the user's wallpaper (put back on a normal exit), and may have made a Start menu entry
/// and a Run-at-sign-in value. Those two are removed only when they point into this install, so a
/// zip copy elsewhere keeps its own.</summary>
public static class UninstallCleanup
{
    /// <summary>Whether Velopack started this process to run an install or uninstall hook. Only
    /// then is Velopack called at all: on any other start it would append to its own log and set its
    /// app model ID, for nothing.</summary>
    public static bool IsInstallerHook(string[] args) =>
        args.Length > 0 && args[0].StartsWith("--veloapp-", StringComparison.OrdinalIgnoreCase);

    public static void Run(string installDir)
    {
        // Velopack has already force-stopped this copy, so its own exit path never ran. It stops only
        // this install, though: a zip copy still running owns the saved wallpaper and is drawing over
        // the desktop, puts both back itself when it exits, and would save its own idea of the
        // shortcut flag over ours.
        if (AnotherCopyRunning(installDir))
        {
            Log.Info("Uninstall: another FeatherWall is running, so its wallpaper and config are left to it");
            Try("remove the Start menu entry", () => RemoveShortcut(installDir));
        }
        else
        {
            Try("restore the wallpaper", () => Engine.RestoreSavedWallpaper());
            Try("restore the desktop", DesktopLayerHost.RestoreDesktop);
            Try("remove the Start menu entry", () => { if (RemoveShortcut(installDir)) ForgetShortcut(); });
        }
        Try("remove the sign-in entry", () => RemoveAutostart(installDir));
    }

    /// <summary>Velopack terminates this install's copy without waiting for it to go, so a message
    /// window owned by this install's exe is that copy on its way out: wait for it, then look again.</summary>
    private static bool AnotherCopyRunning(string installDir)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var window = User32.FindWindowW("FeatherWallMessage", null);
            if (window == IntPtr.Zero) return false;
            User32.GetWindowThreadProcessId(window, out uint pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (!PointsInto(process.MainModule?.FileName, installDir)) return true;
                process.WaitForExit(5000);
            }
            catch (Exception) { Thread.Sleep(200); } // exiting while we looked
        }
        return true;
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
