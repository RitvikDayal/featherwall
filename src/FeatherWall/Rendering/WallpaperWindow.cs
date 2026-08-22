using FeatherWall.Common;
using FeatherWall.Desktop;
using FeatherWall.Interop;
using static FeatherWall.Interop.Win32Constants;

namespace FeatherWall.Rendering;

/// <summary>One borderless surface window per monitor, living inside the wallpaper layer.</summary>
public sealed class WallpaperWindow : Win32Window
{
    public const string ClassName = "FeatherWallSurface";

    public MonitorInfo Monitor { get; }
    public IWallpaperRenderer? Renderer { get; private set; }
    public CompositionHost? Host { get; private set; }

    public WallpaperWindow(MonitorInfo monitor)
    {
        Monitor = monitor;
        CreateWindow(ClassName,
            WS_POPUP | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            monitor.Bounds.Left, monitor.Bounds.Top, monitor.Bounds.Width, monitor.Bounds.Height,
            IntPtr.Zero);
    }

    /// <summary>Raised when this window's GPU device is removed or reset (driver update, TDR).
    /// Forwarded from the composition host so callers do not have to re-subscribe every time a
    /// surface is recreated.</summary>
    public event Action? DeviceLost;

    /// <summary>Creates the composition host — call after the window is attached to the
    /// wallpaper layer. Idempotent, so the DeviceLost forwarding is wired exactly once.</summary>
    public CompositionHost EnsureHost()
    {
        if (Host is not null) return Host;
        var host = new CompositionHost(Hwnd);
        host.DeviceLost += () => DeviceLost?.Invoke();
        Host = host;
        return host;
    }

    public void SetRenderer(IWallpaperRenderer renderer)
    {
        var previous = Renderer;
        Renderer = renderer;
        if (previous is null) return;
        previous.Dispose();
        VideoRenderer.ReclaimMediaPipeline(); // see the note there — Dispose alone strands MF threads
    }

    protected override IntPtr HandleMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_PAINT:
                var hdc = User32.BeginPaint(Hwnd, out var ps);
                try { Renderer?.Paint(hdc); }
                catch (Exception ex) { Log.Error("Paint failed", ex); }
                User32.EndPaint(Hwnd, ref ps);
                return IntPtr.Zero;
            case WM_ERASEBKGND:
                return new IntPtr(1); // renderers own the full surface — avoid flicker
        }
        return base.HandleMessage(msg, wParam, lParam);
    }

    public override void Dispose()
    {
        Renderer?.Dispose();
        Renderer = null;
        Host?.Dispose();
        Host = null;
        base.Dispose();
    }
}
