using Microsoft.Win32;

namespace PrismOS.Desktop;

/// <summary>The sign-in entry: one value under HKCU\...\Run, per user, no admin, removable from
/// the settings window. It launches with <c>--silent</c>, so a login start attaches the wallpaper
/// in the background instead of opening the window.</summary>
public static class Autostart
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "PrismOS";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is not null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe)
            key.SetValue(ValueName, $"\"{exe}\" --silent");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>An earlier version registered the bare exe for login start, which would pop the
    /// settings window at every sign-in. Rewrite the value with --silent once at startup.</summary>
    public static void EnsureSilentFlag()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is not string value || value.Contains("--silent", StringComparison.OrdinalIgnoreCase)) return;
            key.SetValue(ValueName, $"{value} --silent");
        }
        catch { /* best effort; worst case the window opens once at login */ }
    }
}
