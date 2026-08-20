using System.Runtime.InteropServices;

namespace FeatherWall.Interop;

public enum MonitorDpiType
{
    Effective = 0,
    Angular = 1,
    Raw = 2,
}

/// <summary>Per-monitor DPI. GetDpiForWindow reports the DPI of the monitor the window is
/// mostly on, which is meaningless for a window that spans the whole virtual screen — the
/// wallpaper host does exactly that, so every per-monitor size has to come from here.</summary>
public static class Shcore
{
    public const uint DefaultDpi = 96;

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    /// <summary>Effective DPI for a monitor, or 96 if the call fails. Degrading to 96 makes an
    /// unavailable API render exactly as v0.1.0 did rather than throw on the enumerate path.</summary>
    public static uint EffectiveDpi(IntPtr hMonitor)
    {
        try
        {
            if (GetDpiForMonitor(hMonitor, MonitorDpiType.Effective, out uint dpiX, out _) == 0 && dpiX > 0)
                return dpiX;
        }
        catch (DllNotFoundException) { /* pre-8.1; 96 is correct there */ }
        catch (EntryPointNotFoundException) { /* ditto */ }
        return DefaultDpi;
    }
}
