using System.Drawing;
using System.Drawing.Imaging;
using FeatherWall.Common;
using FeatherWall.Config;
using FeatherWall.Desktop;
using FeatherWall.Interop;
using FeatherWall.Rendering;

namespace FeatherWall.Widgets;

/// <summary>The battery halo on its own overlay visual, anchored independently of the info text.
///
/// Exists only when Halo.Detached is set. Attached mode draws the ring into the info widget's own
/// bitmap instead, so exactly one of the two ever paints it — InfoOverlay measures the halo as
/// empty when detached, and this class does not exist when it is not.</summary>
public sealed class HaloOverlay : IDisposable
{
    private const string OverlayKey = "halo";

    private readonly CompositionHost _host;
    private readonly InfoConfig _config;
    private readonly MonitorInfo _monitor;
    private readonly double _dpiScale;
    private readonly BatterySource _battery;
    private readonly Lock _sync = new();

    private SIZE _size;
    private bool _disposed;
    private bool _suspended;

    public HaloOverlay(CompositionHost host, InfoConfig config, MonitorInfo monitor,
                       BatterySource battery, double dpiScale = 1.0)
    {
        _host = host;
        _config = config;
        _monitor = monitor;
        _dpiScale = dpiScale > 0 ? dpiScale : 1.0;
        _battery = battery;

        _battery.Changed += OnBatteryChanged;
        try
        {
            Render();
        }
        catch
        {
            // The source outlives this overlay, so a throw here would otherwise leave a half-built
            // object subscribed and repainting on every battery tick for the life of the process,
            // with the engine catching the exception and never holding a reference to dispose.
            Dispose();
            throw;
        }
    }

    private void OnBatteryChanged()
    {
        try { Render(); }
        catch (Exception ex) { Log.Error("Halo render failed", ex); }
    }

    public void Refresh() => Render();

    /// <summary>Goes silent with the display, as the clock and info overlays do.</summary>
    public void SetSuspended(bool suspended)
    {
        lock (_sync)
        {
            if (_disposed || _suspended == suspended) return;
            _suspended = suspended;
            if (suspended) return;
        }
        Refresh();
    }

    private void Render()
    {
        lock (_sync)
        {
            if (_disposed || _suspended) return;

            var reading = _battery.Current;
            var measured = BatteryHaloRenderer.Measure(_config.Halo, reading, (float)_dpiScale);
            if (measured.IsEmpty)
            {
                // No battery, or switched off. Drop the visual rather than composing an empty one.
                _size = default;
                try { _host.RemoveOverlay(OverlayKey); } catch { /* host may already be gone */ }
                return;
            }

            Log.Info($"Halo repaint: {reading.Percent}% {reading.State}");

            var halo = _config.Halo;
            var size = new SIZE { Cx = measured.Width, Cy = measured.Height };

            // Offsets are relative to the wallpaper window, whose origin is the monitor's top-left.
            // Margins scale with the monitor so the halo keeps the same physical inset on a display
            // with a different DPI.
            var screenPos = ClockLayout.Position(_monitor.WorkArea, size.Cx, size.Cy, halo.Anchor,
                ClockLayout.ScaleMargin(halo.MarginLeft ?? halo.MarginX, _dpiScale),
                ClockLayout.ScaleMargin(halo.MarginTop ?? halo.MarginY, _dpiScale),
                ClockLayout.ScaleMargin(halo.MarginRight ?? halo.MarginX, _dpiScale),
                ClockLayout.ScaleMargin(halo.MarginBottom ?? halo.MarginY, _dpiScale));
            int offsetX = screenPos.X - _monitor.Bounds.Left;
            int offsetY = screenPos.Y - _monitor.Bounds.Top;

            var overlay = _host.GetOverlay(OverlayKey);
            if (overlay is null || size.Cx != _size.Cx || size.Cy != _size.Cy)
            {
                _size = size;
                overlay = _host.CreateOverlay(OverlayKey, size.Cx, size.Cy, offsetX, offsetY);
            }
            else
            {
                overlay.SetOffset(offsetX, offsetY);
            }

            using var bmp = new Bitmap(_size.Cx, _size.Cy, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
                BatteryHaloRenderer.Paint(g, new Rectangle(0, 0, _size.Cx, _size.Cy), halo, reading);
            overlay.PresentBitmap(bmp);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _battery.Changed -= OnBatteryChanged;
            try { _host.RemoveOverlay(OverlayKey); } catch { /* host may already be gone */ }
        }
    }
}
