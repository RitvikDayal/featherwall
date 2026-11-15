using System.Drawing;
using System.Runtime.InteropServices;
using PrismOS.Interop;
using static PrismOS.Interop.Win32Constants;

namespace PrismOS.Tray;

/// <summary>Shell notification-area icon with the PrismOS mark, drawn at run time (no assets).</summary>
public sealed class TrayIcon : IDisposable
{
    public const uint CallbackMessage = WM_APP + 1;
    private const uint IconId = 1;

    /// <summary>SM_CXSMICON: the size the notification area draws at.</summary>
    private const int SM_CXSMICON = 12;

    private readonly IntPtr _ownerHwnd;
    private IntPtr _hIcon;
    private bool _added;

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    public TrayIcon(IntPtr ownerHwnd)
    {
        _ownerHwnd = ownerHwnd;
        _hIcon = DrawPrismIcon();
        Add();
    }

    /// <summary>The mark, drawn at the shell's small icon size.
    ///
    /// The notification area draws the icon at SM_CXSMICON and scales anything else, so an icon
    /// drawn larger loses its edges on the way down. Drawing it here rather than loading the .ico
    /// keeps the tray and the application icon the same shape: both come from PrismIcon.</summary>
    private static IntPtr DrawPrismIcon()
    {
        int size = User32.GetSystemMetrics(SM_CXSMICON);
        if (size <= 0) size = 16;
        using var bitmap = PrismIcon.ToBitmap(size);
        return bitmap.GetHicon();
    }

    private NOTIFYICONDATA BaseData() => new()
    {
        Size = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        Hwnd = _ownerHwnd,
        Id = IconId,
        Tip = "PrismOS",
        Info = "",
        InfoTitle = "",
    };

    public void Add()
    {
        var data = BaseData();
        data.Flags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.CallbackMessage = CallbackMessage;
        data.Icon = _hIcon;
        _added = Shell32.Shell_NotifyIconW(NIM_ADD, ref data);
    }

    /// <summary>Re-add after explorer restarts (TaskbarCreated).</summary>
    public void Refresh()
    {
        Remove();
        Add();
    }

    /// <summary>What clicking the balloon on screen does; set by every ShowBalloon.</summary>
    public BalloonClick Clicks { get; } = new();

    /// <summary><paramref name="onClick"/> runs if this balloon is clicked. Most balloons pass
    /// none, which also clears the previous balloon's action.</summary>
    public void ShowBalloon(string title, string text, Action? onClick = null)
    {
        Clicks.Set(onClick);
        var data = BaseData();
        data.Flags = 0x00000010; // NIF_INFO
        data.InfoTitle = title.Length > 60 ? title[..60] : title;
        data.Info = text.Length > 200 ? text[..200] : text;
        Shell32.Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private void Remove()
    {
        if (!_added) return;
        var data = BaseData();
        Shell32.Shell_NotifyIconW(NIM_DELETE, ref data);
        _added = false;
    }

    public void Dispose()
    {
        Remove();
        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }
}
