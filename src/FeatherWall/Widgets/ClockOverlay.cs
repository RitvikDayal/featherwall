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
    private readonly double _dpiScale;
    private readonly Color _color;
    private readonly Timer _timer;
    private readonly Lock _sync = new();

    private SIZE _size;
    private bool _disposed;
    private bool _suspended;

    /// <summary><paramref name="dpiScale"/> is this monitor's DPI relative to the primary's, so
    /// it is 1.0 on a single-DPI machine and the widget renders exactly as it did in v0.1.0.</summary>
    public ClockOverlay(CompositionHost host, ClockConfig config, MonitorInfo monitor, double dpiScale = 1.0)
    {
        _host = host;
        _config = config;
        _monitor = monitor;
        _dpiScale = dpiScale > 0 ? dpiScale : 1.0;
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

    /// <summary>Stops the tick entirely while the display is off, then repaints once on the way
    /// back so the time is not stale for up to a minute. Not a throttle: a suspended clock does
    /// zero redraws, which is the honest answer to "widgets that go idle" — the whole point is
    /// that Windows pushes this state rather than the clock polling to discover it.</summary>
    public void SetSuspended(bool suspended)
    {
        lock (_sync)
        {
            if (_disposed || _suspended == suspended) return;
            _suspended = suspended;
            if (suspended)
            {
                try { _timer.Change(Timeout.Infinite, Timeout.Infinite); }
                catch (ObjectDisposedException) { }
                return;
            }
        }
        Refresh();
    }

    private void Arm()
    {
        lock (_sync)
        {
            if (_disposed || _suspended) return;
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
            // _suspended as well as _disposed: stopping the timer prevents future scheduling, but
            // a tick already queued before SetSuspended took _sync still arrives and would paint
            // into a display that is off — the exact redraw suspension exists to avoid.
            if (_disposed || _suspended) return;

            var metrics = ClockRenderer.Measure(_config, DateTime.Now, (float)_dpiScale);
            var size = new SIZE { Cx = metrics.Total.Width, Cy = metrics.Total.Height };

            // Overlay offsets are relative to the wallpaper window, whose origin is the
            // monitor's top-left. Margins scale with the monitor so the widget keeps the same
            // physical inset on a display with a different DPI.
            var screenPos = ClockLayout.Position(_monitor.WorkArea, size.Cx, size.Cy, _config.Anchor,
                ClockLayout.ScaleMargin(_config.MarginLeft ?? _config.MarginX, _dpiScale),
                ClockLayout.ScaleMargin(_config.MarginTop ?? _config.MarginY, _dpiScale),
                ClockLayout.ScaleMargin(_config.MarginRight ?? _config.MarginX, _dpiScale),
                ClockLayout.ScaleMargin(_config.MarginBottom ?? _config.MarginY, _dpiScale));
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
