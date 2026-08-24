using System.Drawing;
using System.Drawing.Imaging;
using FeatherWall.Common;
using FeatherWall.Config;
using FeatherWall.Desktop;
using FeatherWall.Interop;
using FeatherWall.Rendering;

namespace FeatherWall.Widgets;

/// <summary>The info widget: a stack of lines fed by system sources, on its own overlay visual
/// anchored independently of the clock.
///
/// There is no timer here, and that is the point. Every repaint originates from a source's
/// Changed event, which Windows raises — a battery percentage moving, a track changing. Between
/// those the widget costs nothing at all, and DWM composes the visual while FeatherWall is
/// idle.</summary>
public sealed class InfoOverlay : IDisposable
{
    private const string OverlayKey = "info";

    private readonly CompositionHost _host;
    private readonly InfoConfig _config;
    private readonly MonitorInfo _monitor;
    private readonly double _dpiScale;
    private readonly Color _color;
    private readonly IReadOnlyList<IWidgetSource> _sources;
    private readonly Lock _sync = new();

    private SIZE _size;
    private bool _disposed;
    private bool _suspended;

    /// <summary><paramref name="sources"/> is already ordered and resolved — the overlay renders
    /// whatever it is handed, in the order it is handed.</summary>
    public InfoOverlay(CompositionHost host, InfoConfig config, MonitorInfo monitor,
                       IReadOnlyList<IWidgetSource> sources, double dpiScale = 1.0)
    {
        _host = host;
        _config = config;
        _monitor = monitor;
        _dpiScale = dpiScale > 0 ? dpiScale : 1.0;
        _color = ClockRenderer.ParseColor(config.Color);
        _sources = sources;

        foreach (var source in _sources) source.Changed += OnSourceChanged;
        Render();
    }

    private void OnSourceChanged()
    {
        try
        {
            Render();
        }
        catch (Exception ex)
        {
            Log.Error("Info widget render failed", ex);
        }
    }

    public void Refresh() => Render();

    /// <summary>Goes silent with the display, as the clock does. A source event that arrives
    /// while suspended still updates the source's value; it just does not paint, and the resume
    /// repaints once with whatever the current values are.</summary>
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

            var values = new string?[_sources.Count];
            for (int i = 0; i < _sources.Count; i++) values[i] = _sources[i].Value;

            var metrics = InfoRenderer.Measure(_config, values, (float)_dpiScale);
            // Every one of these lines is a source event. If they ever appear at a regular
            // interval, a timer has crept in and the feature has failed its cost budget.
            Log.Info($"Info repaint: [{string.Join(" | ", metrics.Lines)}]");
            if (metrics.Lines.Count == 0)
            {
                // Nothing to say: no battery and nothing playing. Drop the visual rather than
                // composing an empty rectangle over the wallpaper forever.
                _size = default;
                try { _host.RemoveOverlay(OverlayKey); } catch { /* host may already be gone */ }
                return;
            }

            var size = new SIZE { Cx = metrics.Total.Width, Cy = metrics.Total.Height };

            // Offsets are relative to the wallpaper window, whose origin is the monitor's
            // top-left. Margins scale with the monitor so the widget keeps the same physical
            // inset on a display with a different DPI.
            var screenPos = ClockLayout.Position(_monitor.WorkArea, size.Cx, size.Cy, _config.Anchor,
                ClockLayout.ScaleMargin(_config.MarginLeft ?? _config.MarginX, _dpiScale),
                ClockLayout.ScaleMargin(_config.MarginTop ?? _config.MarginY, _dpiScale),
                ClockLayout.ScaleMargin(_config.MarginRight ?? _config.MarginX, _dpiScale),
                ClockLayout.ScaleMargin(_config.MarginBottom ?? _config.MarginY, _dpiScale));
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
                InfoRenderer.Paint(g, _config, metrics, _color);
            overlay.PresentBitmap(bmp);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var source in _sources) source.Changed -= OnSourceChanged;
            try { _host.RemoveOverlay(OverlayKey); } catch { /* host may already be gone */ }
        }
    }
}
