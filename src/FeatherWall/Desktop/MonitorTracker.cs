using System.Runtime.InteropServices;
using FeatherWall.Interop;

namespace FeatherWall.Desktop;

public sealed record MonitorInfo(string Device, RECT Bounds, RECT WorkArea, bool Primary, uint Dpi = Shcore.DefaultDpi);

public static class MonitorTracker
{
    public static List<MonitorInfo> Enumerate()
    {
        var monitors = new List<MonitorInfo>();
        User32.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFOEX { Size = (uint)Marshal.SizeOf<MONITORINFOEX>() };
            if (User32.GetMonitorInfoW(hMonitor, ref info))
                monitors.Add(new MonitorInfo(info.Device, info.Monitor, info.Work, (info.Flags & 1) != 0,
                    Shcore.EffectiveDpi(hMonitor)));
            return true;
        };
        User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return monitors;
    }

    /// <summary>Maps a rectangle in virtual-screen coordinates into the client space of a
    /// parent window whose window rect is <paramref name="parentScreenRect"/> (the wallpaper
    /// host spans the virtual screen, so client origin == its screen origin).</summary>
    public static RECT ScreenToParentClient(in RECT screenRect, in RECT parentScreenRect) => new(
        screenRect.Left - parentScreenRect.Left,
        screenRect.Top - parentScreenRect.Top,
        screenRect.Right - parentScreenRect.Left,
        screenRect.Bottom - parentScreenRect.Top);

    /// <summary>How much larger a widget should be drawn on <paramref name="monitor"/> than the
    /// config's authored pixel sizes.
    ///
    /// Deliberately relative to the PRIMARY monitor's DPI rather than to 96. Config values were
    /// authored by looking at the primary display, so this returns exactly 1.0 for it and
    /// v0.1.0 renders pixel-for-pixel unchanged. Scaling against 96 would instead enlarge every
    /// existing 150%-display user's clock by half on upgrade, which is a regression wearing a
    /// fix's clothes, and would need a config migration to undo.</summary>
    public static double DpiScale(MonitorInfo monitor, IReadOnlyList<MonitorInfo> all)
    {
        uint primaryDpi = PrimaryDpi(all);
        if (primaryDpi == 0 || monitor.Dpi == 0) return 1.0;
        return (double)monitor.Dpi / primaryDpi;
    }

    /// <summary>DPI of the primary monitor, falling back to the first monitor and then to 96.
    /// A list with no primary flag set is a real shape during display-topology changes.</summary>
    public static uint PrimaryDpi(IReadOnlyList<MonitorInfo> all)
    {
        if (all is null || all.Count == 0) return Shcore.DefaultDpi;
        foreach (var m in all)
            if (m.Primary && m.Dpi > 0) return m.Dpi;
        // Any real DPI beats the 96 default: a topology change can report [0, 144], and taking
        // all[0] there would scale every widget against a monitor that reported nothing.
        foreach (var m in all)
            if (m.Dpi > 0) return m.Dpi;
        return Shcore.DefaultDpi;
    }
}
