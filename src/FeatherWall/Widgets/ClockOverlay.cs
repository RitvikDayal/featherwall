using System.Drawing;
using System.Drawing.Imaging;
using Timer = System.Threading.Timer;
using FeatherWall.Common;
using FeatherWall.Config;
using FeatherWall.Desktop;
using FeatherWall.Interop;
using FeatherWall.Rendering;

namespace FeatherWall.Widgets;

/// <summary>The time widget: an overlay visual on the monitor's wallpaper composition
/// tree (no window of its own — DWM only reliably composes the monitor-spanning target).
/// Mond-style: large light-weight time, hairline separator, small dimmed date. Rendered
/// with GDI+ and drawn via Direct2D at most once per second — zero per-frame cost over
/// video, click-through by construction.</summary>
public sealed class ClockOverlay : IDisposable
{
    private readonly CompositionHost _host;
    private readonly ClockConfig _config;
    private readonly MonitorInfo _monitor;
    private readonly Color _color;
    private readonly Timer _timer;
    private readonly Lock _sync = new();

    private SIZE _size;
    private bool _disposed;

    public ClockOverlay(CompositionHost host, ClockConfig config, MonitorInfo monitor)
    {
        _host = host;
        _config = config;
        _monitor = monitor;
        _color = ClockRenderer.ParseColor(config.Color);
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        Render();
        Arm();
    }

    public void Refresh()
    {
        Render();
        Arm();
    }

    private void Arm()
    {
        lock (_sync)
        {
            if (_disposed) return;
            try
            {
                _timer.Change(ClockLayout.MillisecondsToNextTick(DateTime.Now, _config.ShowSeconds), Timeout.Infinite);
            }
            catch (ObjectDisposedException) { /* raced Dispose — nothing to do */ }
        }
    }

    private void Tick()
    {
        try
        {
            Render();
        }
        catch (Exception ex)
        {
            Log.Error("Clock render failed", ex);
        }
        finally
        {
            Arm();
        }
    }

    private void Render()
    {
        lock (_sync)
        {
            if (_disposed) return;

            var metrics = ClockRenderer.Measure(_config, DateTime.Now);
            var size = new SIZE { Cx = metrics.Total.Width, Cy = metrics.Total.Height };

            // Overlay offsets are relative to the wallpaper window, whose origin is the
            // monitor's top-left.
            var screenPos = ClockLayout.Position(_monitor.WorkArea, size.Cx, size.Cy, _config.Anchor, _config.MarginX, _config.MarginY);
            int offsetX = screenPos.X - _monitor.Bounds.Left;
            int offsetY = screenPos.Y - _monitor.Bounds.Top;

            var overlay = _host.Overlay;
            if (overlay is null || size.Cx != _size.Cx || size.Cy != _size.Cy)
            {
                _size = size;
                overlay = _host.CreateOverlay(size.Cx, size.Cy, offsetX, offsetY);
            }
            else
            {
                overlay.SetOffset(offsetX, offsetY);
            }

            using var bmp = new Bitmap(_size.Cx, _size.Cy, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
                ClockRenderer.Paint(g, _config, metrics, _color, metrics.Total);
            overlay.PresentBitmap(bmp);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            try { _host.RemoveOverlay(); } catch { /* host may already be gone */ }
        }
    }
}
