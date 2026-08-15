using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using FeatherWall.Interop;
using static FeatherWall.Interop.Win32Constants;

namespace FeatherWall.Tray;

/// <summary>Shell notification-area icon with a runtime-drawn feather glyph (no assets).</summary>
public sealed class TrayIcon : IDisposable
{
    public const uint CallbackMessage = WM_APP + 1;
    private const uint IconId = 1;

    private readonly IntPtr _ownerHwnd;
    private IntPtr _hIcon;
    private bool _added;

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    public TrayIcon(IntPtr ownerHwnd)
    {
        _ownerHwnd = ownerHwnd;
        _hIcon = DrawFeatherIcon();
        Add();
    }

    private static IntPtr DrawFeatherIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var back = new LinearGradientBrush(new Rectangle(0, 0, 32, 32),
                Color.FromArgb(255, 56, 120, 220), Color.FromArgb(255, 110, 190, 255), 45f);
            g.FillEllipse(back, 1, 1, 30, 30);
            using var quill = new Pen(Color.White, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(quill, 9, 23, 23, 9);           // quill shaft
            g.DrawCurve(quill, new PointF[] { new(23, 9), new(20, 16), new(13, 21) }); // vane
            using var barb = new Pen(Color.White, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(barb, 13, 19, 17, 19);
            g.DrawLine(barb, 15, 15, 19, 15);
        }
        return bmp.GetHicon();
    }

    private NOTIFYICONDATA BaseData() => new()
    {
        Size = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        Hwnd = _ownerHwnd,
        Id = IconId,
        Tip = "FeatherWall",
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

    public void ShowBalloon(string title, string text)
    {
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
