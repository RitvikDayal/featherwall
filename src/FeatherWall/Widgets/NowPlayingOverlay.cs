using System.Drawing;
using System.Drawing.Imaging;
using Timer = System.Threading.Timer;
using FeatherWall.Common;
using FeatherWall.Config;
using FeatherWall.Desktop;
using FeatherWall.Interop;
using FeatherWall.Rendering;
using FeatherWall.Widgets.Themes;

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

    private readonly CompositionHost _host;
    private readonly InfoConfig _config;
    private readonly MonitorInfo _monitor;
    private readonly double _dpiScale;
    private readonly NowPlayingSource _source;
    private readonly WidgetStyle _style;
    private readonly Timer _timer;
    private readonly Lock _sync = new();

    private SIZE _size;
    private Bitmap? _face;
    private string? _faceTrackId;

    /// <summary>Whether the cached face was built with artwork.
    ///
    /// Artwork is fetched asynchronously, so it routinely lands *after* the first paint of the
    /// track it belongs to. Keying staleness on the track alone meant that face was kept — same
    /// track, so not stale — and the record showed the flat fallback for the rest of that song.</summary>
    private bool _faceHasArt;
    private float _turns;
    private bool _disposed;
    private bool _suspended;   // display off
    private bool _covered;     // a window is over the desktop
    private bool _ticking;

    /// <summary>Temporary: true runs the v0.2.0 15 fps timer instead of the compositor, so one
    /// build can be measured both ways (<c>--spin-timer</c>). Deleted once the measurement picks one.</summary>
    internal static bool UseTimerSpin;

    // Compositor path: the face on a layer the compositor turns, rim and words on a layer above.
    private CompositionSurface? _faceLayer;
    private CompositionSurface? _staticLayer;
    private CompositorSpin? _spin;
    private float _heldAngle;
    private (string? Track, bool Art, bool Playing, int Side)? _presentedFace;

    public NowPlayingOverlay(CompositionHost host, InfoConfig config, MonitorInfo monitor,
                             NowPlayingSource source, double dpiScale = 1.0, WidgetStyle? style = null)
    {
        _host = host;
        _config = config;
        _monitor = monitor;
        _dpiScale = dpiScale > 0 ? dpiScale : 1.0;
        _source = source;
        _style = style ?? WidgetStyle.Classic;
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
        if (!UseTimerSpin)
        {
            UpdateSpin();
            return;
        }

        bool shouldTick;
        lock (_sync)
        {
            shouldTick = RecordSpin.ShouldTurn(_disposed, _suspended, _covered, _config.Disc.Rotate,
                                               _source.Current.IsPlaying, _size.Cx > 0 && _size.Cy > 0);

            if (shouldTick == _ticking) return;
            _ticking = shouldTick;
        }

        try
        {
            if (shouldTick)
            {
                Log.Info("Now-playing: record turning");
                _timer.Change(0, 1000 / RecordSpin.TimerFramesPerSecond);
            }
            else
            {
                Log.Info("Now-playing: record still");
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
        catch (ObjectDisposedException) { /* raced Dispose */ }
    }

    /// <summary>The compositor path's UpdateTimer. Decides and acts under one lock, so a second
    /// caller cannot start the spin between another's decision to stop it and the stop.</summary>
    private void UpdateSpin()
    {
        lock (_sync)
        {
            bool shouldTurn = RecordSpin.ShouldTurn(_disposed, _suspended, _covered, _config.Disc.Rotate,
                                                    _source.Current.IsPlaying, _size.Cx > 0 && _size.Cy > 0);
            if (shouldTurn == _ticking) return;
            _ticking = shouldTurn;

            if (shouldTurn)
            {
                Log.Info("Now-playing: record turning");
                _spin?.Start(_heldAngle);
            }
            else
            {
                Log.Info("Now-playing: record still");
                if (_spin is { Running: true }) _heldAngle = _spin.Stop();
            }
        }
    }

    private void Tick()
    {
        try
        {
            lock (_sync)
            {
                if (_disposed || !_ticking) return;
                _turns = (_turns + RecordSpin.DegreesPerSecond / 360f / RecordSpin.TimerFramesPerSecond) % 1f;
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
            var metrics = IsEditorial
                ? Editorial.MeasureRecord(_config.Disc, reading, ThemeScale)
                : NowPlayingRenderer.Measure(_config.Disc, reading, (float)_dpiScale);
            if (metrics.Total.IsEmpty)
            {
                Log.Info($"Record: nothing to draw (title={reading.Title ?? "<none>"}, playing={reading.IsPlaying})");
                _size = default;
                DropLayers();
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

            if (!UseTimerSpin)
            {
                PresentLayered(metrics, reading, size, offsetX, offsetY);
                return;
            }

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
                if (IsEditorial)
                {
                    if (_face is not null && !metrics.DiscBox.IsEmpty)
                        DiscRenderer.PaintFace(g, metrics.DiscBox, _face, _turns, reading.IsPlaying);
                    Editorial.PaintRecordStatic(g, metrics, _config.Disc, reading, (float)reading.Progress,
                                                _style.Accent, ThemeScale);
                }
                else
                {
                    NowPlayingRenderer.Paint(g, metrics, _config.Disc, reading, _face, _turns,
                                             (float)reading.Progress, (float)_dpiScale);
                }
            }
            if (IsEditorial) BakedShadow.DrawBehind(bmp, ShadowRecipe.Default, ThemeScale);
            overlay.PresentBitmap(bmp);
        }
    }

    /// <summary>Compositor path. The face is presented only when what it shows changes — track,
    /// artwork, size, or paused dim — and the compositor turns it in between; the rim and the words
    /// are repainted on every source event, as before. Caller holds <see cref="_sync"/>.</summary>
    private void PresentLayered(in NowPlayingRenderer.NowPlayingMetrics metrics, NowPlayingReading reading,
                                SIZE size, int offsetX, int offsetY)
    {
        var disc = metrics.DiscBox;
        var group = _host.GetOverlayGroup(OverlayKey);
        if (group is null || _faceLayer is null || _staticLayer is null || size.Cx != _size.Cx || size.Cy != _size.Cy)
        {
            DropLayers();
            _size = size;
            group = _host.CreateOverlayGroup(OverlayKey, offsetX, offsetY);
            _faceLayer = group.AddLayer(Math.Max(disc.Width, 1), Math.Max(disc.Height, 1), disc.X, disc.Y);
            _staticLayer = group.AddLayer(size.Cx, size.Cy, 0, 0);
            if (_config.Disc.Rotate && !disc.IsEmpty)
            {
                _spin = new CompositorSpin(_host, _faceLayer.Visual, disc.Width / 2f, disc.Height / 2f);
                _spin.Hold(_heldAngle);
            }
        }
        else
        {
            group.SetOffset(offsetX, offsetY);
        }

        if (_face is not null && !disc.IsEmpty)
        {
            var key = (_faceTrackId, _faceHasArt, reading.IsPlaying, disc.Width);
            if (_presentedFace != key)
            {
                using var faceBmp = new Bitmap(disc.Width, disc.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(faceBmp))
                    DiscRenderer.PaintFace(g, new Rectangle(0, 0, disc.Width, disc.Height), _face, 0f, reading.IsPlaying);
                _faceLayer.PresentBitmap(faceBmp);
                _presentedFace = key;
            }
        }

        using var bmp = new Bitmap(_size.Cx, _size.Cy, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            if (IsEditorial)
                Editorial.PaintRecordStatic(g, metrics, _config.Disc, reading, (float)reading.Progress,
                                            _style.Accent, ThemeScale);
            else
                NowPlayingRenderer.Paint(g, metrics, _config.Disc, reading, _face, 0f, (float)reading.Progress,
                                         (float)_dpiScale, includeFace: false);
        }
        // The face turns on the layer beneath, so its shadow is cast here and cleared inside it.
        if (IsEditorial)
            BakedShadow.DrawBehind(bmp, ShadowRecipe.Default, ThemeScale,
                                   disc.IsEmpty || _face is null ? null : disc);
        _staticLayer.PresentBitmap(bmp);
    }

    /// <summary>Forgets the compositor path's layers, keeping the angle so a rebuilt record carries
    /// on from where it was. Clears <see cref="_ticking"/> so the next UpdateSpin starts the new
    /// layers turning if it should. Caller holds <see cref="_sync"/>.</summary>
    private void DropLayers()
    {
        if (_spin is not null)
        {
            _heldAngle = _spin.Stop();
            _spin.Dispose();
            _spin = null;
        }
        _ticking = false;
        _faceLayer = null;
        _staticLayer = null;
        _presentedFace = null;
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

        // Artwork arriving for a track already on screen counts as stale. It does not go the
        // other way: art never disappears mid-track, so a face that has art is not rebuilt just
        // because a later read returned none.
        bool stale = _face is null
                     || _faceTrackId != reading.TrackId
                     || _face.Width != side
                     || (!_faceHasArt && _source.Art is not null);
        if (!stale) return;

        var art = _source.Art;
        DisposeFace();
        _face = DiscRenderer.RenderFace(side, art,
            IsEditorial ? _style.Accent : ClockRenderer.ParseColor(_config.Disc.AccentColor));
        _faceTrackId = reading.TrackId;
        _faceHasArt = art is not null;
    }

    private bool IsEditorial => _style.Theme == WidgetTheme.Editorial;
    private float ThemeScale => _style.Size * (float)_dpiScale;

    private void DisposeFace()
    {
        _face?.Dispose();
        _face = null;
        _faceTrackId = null;
        _faceHasArt = false;
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
            DropLayers();
            DisposeFace();
            try { _host.RemoveOverlay(OverlayKey); } catch { /* host may already be gone */ }
        }
    }
}
