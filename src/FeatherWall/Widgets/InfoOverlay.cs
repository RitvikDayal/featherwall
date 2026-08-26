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

    /// <summary>Null when "battery" is not among the configured sources. The halo follows the
    /// source: drawing a ring for something the user removed from the list would be wrong.
    ///
    /// Passed separately rather than found among the sources, because IWidgetSource is a string
    /// and an event — widening it for the one implementation that has a structured reading would
    /// leave every future source carrying a battery-shaped hole.</summary>
    private readonly BatterySource? _battery;
    private readonly Lock _sync = new();

    private SIZE _size;
    private bool _disposed;
    private bool _suspended;

    /// <summary><paramref name="sources"/> is already ordered and resolved — the overlay renders
    /// whatever it is handed, in the order it is handed.</summary>
    public InfoOverlay(CompositionHost host, InfoConfig config, MonitorInfo monitor,
                       IReadOnlyList<IWidgetSource> sources, BatterySource? battery = null,
                       double dpiScale = 1.0)
    {
        _host = host;
        _config = config;
        _monitor = monitor;
        _dpiScale = dpiScale > 0 ? dpiScale : 1.0;
        _color = ClockRenderer.ParseColor(config.Color);
        _sources = sources;
        _battery = battery;

        foreach (var source in _sources) source.Changed += OnSourceChanged;
        try
        {
            Render();
        }
        catch
        {
            // The sources outlive this overlay, so a throw here would leave a half-built object
            // subscribed to them and repainting on every battery tick for the life of the
            // process. The engine catches and logs; it never sees the reference to dispose.
            Dispose();
            throw;
        }
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

            var reading = _battery?.Current ?? default;
            // Detached mode draws the halo in its own overlay, so this one must not draw it too.
            var haloSize = _config.Halo.Detached
                ? Size.Empty
                : BatteryHaloRenderer.Measure(_config.Halo, reading, (float)_dpiScale);

            // The halo now prints the percentage inside the ring, so the battery's text line
            // repeats it — "97" in a circle beside "97% charging". Whenever a halo is drawn at
            // all, attached or detached, it owns the battery and the words step aside.
            if (_battery is not null && _config.Halo.Enabled && reading.State != BatteryState.None)
                for (int i = 0; i < _sources.Count; i++)
                    if (ReferenceEquals(_sources[i], _battery)) values[i] = null;

            var metrics = InfoRenderer.Measure(_config, values, (float)_dpiScale, haloSize);
            // Every one of these lines is a source event. If they ever appear at a regular
            // interval, a timer has crept in and the feature has failed its cost budget.
            Log.Info($"Info repaint: [{string.Join(" | ", metrics.Lines)}]{(haloSize.IsEmpty ? "" : " +halo")}");

            // Total rather than Lines: a halo with the text switched off is still something to draw.
            if (metrics.Total.IsEmpty)
            {
                // Nothing to say at all. Drop the visual rather than composing an empty rectangle
                // over the wallpaper forever.
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
            {
                InfoRenderer.Paint(g, _config, metrics, _color);
                if (!metrics.HaloBox.IsEmpty)
                    BatteryHaloRenderer.Paint(g, metrics.HaloBox, _config.Halo, reading);
            }
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
