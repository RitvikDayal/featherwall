using System.Runtime.InteropServices;

namespace FeatherWall.Interop;

public static class Gdi32
{
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr dstDc, int x, int y, int cx, int cy, IntPtr srcDc, int x1, int y1, uint rop);

    public const uint SRCCOPY = 0x00CC0020;
}

public static class Shell32
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATA data);

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int state);

    // QUNS values
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
}

public static class Kernel32
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("kernel32.dll")]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AttachConsole(uint processId);

    public const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
}

public static class WtsApi32
{
    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSRegisterSessionNotification(IntPtr hwnd, uint flags);

    [DllImport("wtsapi32.dll")]
    public static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);

    public const uint NOTIFY_FOR_THIS_SESSION = 0;
}

public static class ComDlg32
{
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetOpenFileNameW(ref OPENFILENAME ofn);

    public const uint OFN_FILEMUSTEXIST = 0x00001000;
    public const uint OFN_PATHMUSTEXIST = 0x00000800;
    public const uint OFN_NOCHANGEDIR = 0x00000008;
}
