using System.Drawing;
using System.Drawing.Imaging;
using Timer = System.Threading.Timer;
using FeatherWall.Common;
using FeatherWall.Config;
using FeatherWall.Desktop;
using FeatherWall.Interop;
using FeatherWall.Rendering;

namespace FeatherWall.Widgets;

/// <summary>The now-playing block on its own overlay visual: the record, the title, the artist.
///
/// This is the only thing in FeatherWall that owns a frame clock, and it is deliberately hard to
/// leave running. The timer exists only while all of these hold:
///
///   * something is actually playing (not paused, not stopped),
///   * the desktop is visible (no window covering it),
///   * the display is on,
///   * and <see cref="DiscConfig.Rotate"/> is set.
///
/// Any of those going false stops it dead rather than throttling it. With Rotate off no timer is
/// ever created and the whole design renders as a still image.</summary>
public sealed class NowPlayingOverlay : IDisposable
{
    private const string OverlayKey = "nowplaying";

    /// <summary>15 fps. A record at 33⅓ rpm turns 13° per frame at this rate, which reads as
    /// motion; going higher spends frames nobody can see on a wallpaper.</summary>
    private const int FramesPerSecond = 15;

    /// <summary>33⅓ rpm, as an LP.</summary>
    private const float TurnsPerSecond = 0.5556f;

    private readonly CompositionHost _host;
    private readonly InfoConfig _config;
    private readonly MonitorInfo _monitor;
    private readonly double _dpiScale;
    private readonly NowPlayingSource _source;
    private readonly Timer _timer;
    private readonly Lock _sync = new();

    private SIZE _size;
    private Bitmap? _face;
    private string? _faceTrackId;
    private float _turns;
    private bool _disposed;
    private bool _suspended;   // display off
    private bool _covered;     // a window is over the desktop
    private bool _ticking;

    public NowPlayingOverlay(CompositionHost host, InfoConfig config, MonitorInfo monitor,
                             NowPlayingSource source, double dpiScale = 1.0)
    {
        _host = host;
        _config = config;
        _monitor = monitor;
        _dpiScale = dpiScale > 0 ? dpiScale : 1.0;
        _source = source;
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);

        _source.Changed += OnSourceChanged;
        try
        {
            Render();
            UpdateTimer();
        }
        catch
        {
            // The source outlives this overlay, so a throw here would leave a half-built object
            // subscribed and repainting for the life of the process.
            Dispose();
            throw;
        }
    }

    private void OnSourceChanged()
    {
        try
        {
            Render();
            UpdateTimer();
        }
        catch (Exception ex)
        {
            Log.Error("Now-playing render failed", ex);
        }
    }

    public void Refresh()
    {
        Render();
        UpdateTimer();
    }

    public void SetSuspended(bool suspended)
    {
        lock (_sync)
        {
            if (_disposed || _suspended == suspended) return;
            _suspended = suspended;
        }
        UpdateTimer();
        if (!suspended) Refresh();
    }

    /// <summary>A window is over the desktop, so nothing here is visible. Reuses the signal the
    /// engine already computes for pausing video — the record should stop for exactly the reasons
    /// the wallpaper does.</summary>
    public void SetCovered(bool covered)
    {
        lock (_sync)
        {
            if (_disposed || _covered == covered) return;
            _covered = covered;
        }
        UpdateTimer();
    }

    /// <summary>Starts or stops the frame clock. Called after anything that could change the
    /// answer, and it is the only place the timer is armed.</summary>
    private void UpdateTimer()
    {
        bool shouldTick;
        lock (_sync)
        {
            shouldTick = !_disposed && !_suspended && !_covered
                         && _config.Disc.Rotate
                         && _source.Current.IsPlaying
                         && _size.Cx > 0 && _size.Cy > 0;

            if (shouldTick == _ticking) return;
            _ticking = shouldTick;
        }

        try
        {
            if (shouldTick)
            {
                Log.Info("Now-playing: record turning");
                _timer.Change(0, 1000 / FramesPerSecond);
            }
            else
            {
                Log.Info("Now-playing: record still");
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
        catch (ObjectDisposedException) { /* raced Dispose */ }
    }

    private void Tick()
    {
        try
        {
            lock (_sync)
            {
                if (_disposed || !_ticking) return;
                _turns = (_turns + TurnsPerSecond / FramesPerSecond) % 1f;
            }
            Render();
        }
        catch (Exception ex)
        {
            Log.Error("Now-playing tick failed", ex);
        }
    }

    private void Render()
    {
        lock (_sync)
        {
            if (_disposed || _suspended) return;

            var reading = _source.Current;
            var metrics = NowPlayingRenderer.Measure(_config.Disc, reading, (float)_dpiScale);
            if (metrics.Total.IsEmpty)
            {
                Log.Info($"Record: nothing to draw (title={reading.Title ?? "<none>"}, playing={reading.IsPlaying})");
                _size = default;
                DisposeFace();
                try { _host.RemoveOverlay(OverlayKey); } catch { /* host may already be gone */ }
                return;
            }

            EnsureFace(reading, metrics.DiscBox.Width);

            var size = new SIZE { Cx = metrics.Total.Width, Cy = metrics.Total.Height };
            var disc = _config.Disc;
            var screenPos = ClockLayout.Position(_monitor.WorkArea, size.Cx, size.Cy, disc.Anchor,
                ClockLayout.ScaleMargin(disc.MarginLeft ?? disc.MarginX, _dpiScale),
                ClockLayout.ScaleMargin(disc.MarginTop ?? disc.MarginY, _dpiScale),
                ClockLayout.ScaleMargin(disc.MarginRight ?? disc.MarginX, _dpiScale),
                ClockLayout.ScaleMargin(disc.MarginBottom ?? disc.MarginY, _dpiScale));
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
                NowPlayingRenderer.Paint(g, metrics, _config.Disc, reading, _face, _turns, 0f, (float)_dpiScale);
            overlay.PresentBitmap(bmp);
        }
    }

    /// <summary>The record's face is composited once per track. Rebuilding it every frame would
    /// mean redrawing the artwork and forty groove circles fifteen times a second.</summary>
    private void EnsureFace(NowPlayingReading reading, int side)
    {
        if (side <= 0)
        {
            DisposeFace();
            return;
        }

        bool stale = _face is null || _faceTrackId != reading.TrackId || _face.Width != side;
        if (!stale) return;

        DisposeFace();
        _face = DiscRenderer.RenderFace(side, _source.Art, ClockRenderer.ParseColor(_config.Disc.AccentColor));
        _faceTrackId = reading.TrackId;
    }

    private void DisposeFace()
    {
        _face?.Dispose();
        _face = null;
        _faceTrackId = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _ticking = false;
            _source.Changed -= OnSourceChanged;
            _timer.Dispose();
            DisposeFace();
            try { _host.RemoveOverlay(OverlayKey); } catch { /* host may already be gone */ }
        }
    }
}
