using System.Runtime.InteropServices;

namespace FeatherWall.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct POWERBROADCAST_SETTING
{
    public Guid PowerSetting;
    public uint DataLength;
    public byte Data; // first byte; every setting used here is a single DWORD whose low byte carries the state
}

/// <summary>Push notifications for the power and display state the wallpaper cares about.
///
/// The point is that none of this is polled. Windows already knows the display went dark or the
/// laptop came off AC, and it will tell a window that asks — so the wallpaper stops rendering
/// into a screen nobody is looking at without burning a timer to discover that.</summary>
public sealed class PowerNotifications : IDisposable
{
    /// <summary>Monitor on/off for the *session*, including the "dimmed" state.
    /// Deliberately NOT GUID_MONITOR_POWER_ON: Microsoft superseded it ("Windows 8 and Windows
    /// Server 2012: New applications should use GUID_CONSOLE_DISPLAY_STATE instead") and it
    /// reports only the primary monitor, which is the wrong shape for a per-monitor product.</summary>
    public static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    /// <summary>User present / inactive for the session.</summary>
    public static readonly Guid SessionDisplayStatus = new("2b84c20e-ad23-4ddf-93db-05ffbd7efca5");

    /// <summary>AC vs battery vs short-term (UPS).</summary>
    public static readonly Guid AcDcPowerSource = new("5d3e9a59-e9D5-4b00-a6bd-ff34ff516548");

    /// <summary>Battery-saver on/off.</summary>
    public static readonly Guid PowerSavingStatus = new("E00958C0-C213-4ACE-AC77-FECCED2EEEA5");

    public const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;

    /// <summary>Values of GUID_CONSOLE_DISPLAY_STATE.</summary>
    public const byte DisplayOff = 0;
    public const byte DisplayOn = 1;
    public const byte DisplayDimmed = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSettingGuid, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll")]
    private static extern nuint VirtualQuery(IntPtr address, out MEMORY_BASIC_INFORMATION buffer, nuint length);

    private const uint MEM_COMMIT = 0x1000;
    private const uint PAGE_NOACCESS = 0x01;
    private const uint PAGE_GUARD = 0x100;

    private readonly List<IntPtr> _handles = [];
    private bool _disposed;

    public PowerNotifications(IntPtr hwnd)
    {
        foreach (var guid in new[] { ConsoleDisplayState, SessionDisplayStatus, AcDcPowerSource, PowerSavingStatus })
        {
            var copy = guid;
            var handle = RegisterPowerSettingNotification(hwnd, ref copy, DEVICE_NOTIFY_WINDOW_HANDLE);
            if (handle != IntPtr.Zero) _handles.Add(handle);
            else Common.Log.Warn($"RegisterPowerSettingNotification failed for {guid} (win32 {Marshal.GetLastWin32Error()})");
        }
    }

    /// <summary>True when the whole span sits in one committed, readable region of this process.
    ///
    /// WM_POWERBROADCAST is below WM_USER, so Windows will not marshal its lParam across a process
    /// boundary — any local process at this integrity level can SendMessage a PBT_POWERSETTINGCHANGE
    /// carrying an arbitrary pointer, and dereferencing it takes the process down with an access
    /// violation that no catch block can intercept. VirtualQuery is the screen, checked before the
    /// only dereference on this path.
    ///
    /// Not a security boundary and not sold as one: a same-integrity caller can already terminate
    /// FeatherWall outright. It stops a wild pointer from turning a stray message into a crash.</summary>
    private static bool Readable(IntPtr address, int bytes)
    {
        if (address == IntPtr.Zero || bytes <= 0) return false;

        var size = (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();
        if (VirtualQuery(address, out var info, size) == 0) return false;
        if (info.State != MEM_COMMIT) return false;
        if ((info.Protect & (PAGE_GUARD | PAGE_NOACCESS)) != 0) return false;

        ulong start = (ulong)address;
        ulong regionStart = (ulong)info.BaseAddress;
        ulong regionEnd = regionStart + (ulong)info.RegionSize;
        return start >= regionStart && start + (ulong)bytes <= regionEnd;
    }

    /// <summary>Reads a WM_POWERBROADCAST / PBT_POWERSETTINGCHANGE lParam. Returns false rather than
    /// guessing whenever the payload is not one this process can trust: an unreadable pointer, or a
    /// setting whose data is narrower than the DWORD every setting registered here actually carries.
    /// A short DataLength used to be accepted and its first byte returned, which invented a display
    /// or power state out of a truncated payload.</summary>
    public static bool TryRead(IntPtr lParam, out Guid setting, out byte value)
    {
        setting = Guid.Empty;
        value = 0;
        if (!Readable(lParam, Marshal.SizeOf<POWERBROADCAST_SETTING>())) return false;

        var payload = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);
        if (payload.DataLength < sizeof(uint)) return false;

        setting = payload.PowerSetting;
        value = payload.Data;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Leaks a kernel object per handle if skipped, and these outlive the window otherwise.
        foreach (var handle in _handles) UnregisterPowerSettingNotification(handle);
        _handles.Clear();
    }
}
