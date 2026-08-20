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

    /// <summary>Reads a WM_POWERBROADCAST / PBT_POWERSETTINGCHANGE lParam. Returns false when the
    /// payload is not a single-byte-readable setting rather than guessing at a value.</summary>
    public static bool TryRead(IntPtr lParam, out Guid setting, out byte value)
    {
        setting = Guid.Empty;
        value = 0;
        if (lParam == IntPtr.Zero) return false;
        var payload = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);
        if (payload.DataLength < 1) return false;
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
