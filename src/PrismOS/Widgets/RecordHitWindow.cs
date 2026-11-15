using System.Runtime.InteropServices;
using PrismOS.Interop;
using PrismOS.Common;
using static PrismOS.Interop.Win32Constants;

namespace PrismOS.Widgets;

/// <summary>An invisible, clickable window over the transport buttons of the music pill. The
/// desktop widgets themselves are composition layers (no window, click-through by construction),
/// so the buttons need their own window to receive clicks.
///
/// It paints nothing, lives at the same screen rect as the buttons, and only answers
/// WM_LBUTTONDOWN. Alpha is zero, but the window is not WS_EX_TRANSPARENT, so it is clickable
/// while being invisible. Click-through is the default everywhere else.</summary>
public sealed class RecordHitWindow : Win32Window
{
    private const string ClassName = "PrismOSMediaHitTarget";
    private const int SwHide = 0, SwShow = 5;
    private const uint LwaAlpha = 0x2;

    private Rectangle _band;
    private readonly Action<int> _onButton;
    private IReadOnlyList<Rectangle> _buttons = [];

    public RecordHitWindow(Action<int> onButton)
    {
        _onButton = onButton;
        CreateWindow(ClassName, WS_POPUP, WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            0, 0, 1, 1, IntPtr.Zero);
        User32.SetLayeredWindowAttributes(Hwnd, 0, 0, LwaAlpha);
    }

    /// <summary>Places the window over the buttons' union, in screen coordinates. Clicks are
    /// mapped back against <paramref name="screenButtons"/>. A hidden (zero-sized) window is
    /// used when the record is not on screen.</summary>
    public void Reposition(Rectangle band, IReadOnlyList<Rectangle> screenButtons)
    {
        _band = band;
        _buttons = screenButtons;
        if (band.Width <= 0 || band.Height <= 0)
        {
            if (Hwnd != IntPtr.Zero) User32.ShowWindow(Hwnd, SwHide);
            return;
        }
        User32.SetWindowPos(Hwnd, IntPtr.Zero, band.X, band.Y, band.Width, band.Height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        User32.SetLayeredWindowAttributes(Hwnd, 0, 0, LwaAlpha);
        PlaceAboveWallpaper(band);
    }

    public void Hide() => User32.ShowWindow(Hwnd, SwHide);

    /// <summary>The wallpaper window covers the whole monitor, so left at the bottom of the
    /// z-order chain the click target never sees a mouse message. Insert the hit window
    /// directly above the wallpaper it covers (and below any application above the wallpaper),
    /// which is exactly where the buttons should be clickable but nothing else.</summary>
    private void PlaceAboveWallpaper(Rectangle band)
    {
        var center = new PointF(band.X + band.Width / 2f, band.Y + band.Height / 2f);
        IntPtr prev = IntPtr.Zero;
        while (true)
        {
            prev = User32.FindWindowExW(IntPtr.Zero, prev, Rendering.WallpaperWindow.ClassName, null);
            if (prev == IntPtr.Zero) return;
            if (User32.GetWindowRect(prev, out var r) && center.X >= r.Left && center.X < r.Right && center.Y >= r.Top && center.Y < r.Bottom)
            {
                User32.SetWindowPos(Hwnd, prev, 0, 0, 0, 0,
                    SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                return;
            }
        }
    }

    protected override IntPtr HandleMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == User32.WM_LBUTTONDOWN)
        {
            int packed = lParam.ToInt32();
            int sx = _band.X + (short)(packed & 0xFFFF);
            int sy = _band.Y + (short)((packed >> 16) & 0xFFFF);
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (_buttons[i].Contains(new Point(sx, sy)))
                {
                    _onButton(i);
                    return IntPtr.Zero;
                }
            }
            return IntPtr.Zero;
        }
        return base.HandleMessage(msg, wParam, lParam);
    }
}
