using System.Runtime.InteropServices;
using FeatherWall.Interop;

namespace FeatherWall.Desktop;

public sealed record MonitorInfo(string Device, RECT Bounds, RECT WorkArea, bool Primary);

public static class MonitorTracker
{
    public static List<MonitorInfo> Enumerate()
    {
        var monitors = new List<MonitorInfo>();
        User32.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFOEX { Size = (uint)Marshal.SizeOf<MONITORINFOEX>() };
            if (User32.GetMonitorInfoW(hMonitor, ref info))
                monitors.Add(new MonitorInfo(info.Device, info.Monitor, info.Work, (info.Flags & 1) != 0));
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
}
