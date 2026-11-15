using System.Drawing;
using System.Drawing.Imaging;
using Timer = System.Threading.Timer;
using PrismOS.Common;
using PrismOS.Config;
using PrismOS.Desktop;
using PrismOS.Interop;
using PrismOS.Rendering;
using PrismOS.Widgets.Themes;

namespace PrismOS.Widgets;

/// <summary>The now-playing pill on its own overlay visual: album art (with equalizer bars while
/// something plays), the source app over the title over the artist, and working previous /
/// play-pause / next buttons.
///
/// Clicks on the buttons are delivered by an invisible clickable window
/// (<see cref="RecordHitWindow"/>) — the composition tree itself is click-through by
/// construction, so the buttons cannot be hit from it. The hit window paints nothing and moves
/// with the pill.
///
/// The bars animate on the same 15 fps timer the old record used, and only while all of these
/// hold: something is playing, the desktop is visible, the display is on, and Disc.Rotate
/// (re-purposed as the animation switch) is set.</summary>
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

    private RecordHitWindow? _hitWindow;
    private SIZE _size;
    private float _phase;
    private readonly Func<MonitorInfo, string?>? _backdrop;
    private readonly bool _blur;
    private bool _disposed;
    private bool _suspended;   // display off
    private bool _covered;     // a window is over the desktop
    private bool _ticking;

    public NowPlayingOverlay(CompositionHost host, InfoConfig config, MonitorInfo monitor,
                             NowPlayingSource source, double dpiScale = 1.0, WidgetStyle? style = null,
                             Func<MonitorInfo, string?>? backdrop = null, bool blur = true)
    {
        _host = host;
        _config = config;
        _monitor = monitor;
        _dpiScale = dpiScale > 0 ? dpiScale : 1.0;
        _source = source;
        _backdrop = backdrop;
        _blur = blur;
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
    /// engine already computes for pausing video — the widget should stop for exactly the reasons
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

    /// <summary>Starts or stops the frame clock. The only place the timer is armed.</summary>
    private void UpdateTimer()
    {
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
            if (shouldTick) _timer.Change(0, 1000 / RecordSpin.TimerFramesPerSecond);
            else _timer.Change(Timeout.Infinite, Timeout.Infinite);
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
                _phase = (Environment.TickCount64 / 150f) % (MathF.PI * 2f * 4f); // slow wobble, shared with the bars
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
            if (DiscRenderer.Measure(_config.Disc, reading).IsEmpty)
            {
                _size = default;
                try { _host.RemoveOverlay(OverlayKey); } catch { /* host may already be gone */ }
                _hitWindow?.Hide();
                return;
            }

            float scale = ThemeScale;
            using var measure = Graphics.FromHwnd(IntPtr.Zero);
            var metrics = MusicStrip.Measure(measure, reading,
                NowPlayingSource.FriendlySourceName(_source.SourceApp), scale);
            int pad = (int)Math.Ceiling(ShadowRecipe.Default.Pad * Math.Max(scale, 0.05f));
            var size = new SIZE { Cx = metrics.Size.Width + pad * 2, Cy = metrics.Size.Height + pad * 2 };

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

            using var bmp = new Bitmap(size.Cx, size.Cy, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                if (_blur && _backdrop?.Invoke(_monitor) is { } still && File.Exists(still))
                    PanelBlur.ApplyUnderPanel(g, _monitor, still,
                        new Rectangle(screenPos.X - _monitor.Bounds.Left, screenPos.Y - _monitor.Bounds.Top, size.Cx, size.Cy),
                        [new PanelBlur.Region(new RectangleF(pad, pad, metrics.Size.Width, metrics.Size.Height), metrics.Size.Height * 0.32f)]);
                g.TranslateTransform(pad, pad);
                MusicStrip.Paint(g, metrics, reading, _source.Art, scale, _phase, _blur ? 150 : 216);
                g.ResetTransform();
                BakedShadow.DrawBehind(bmp, ShadowRecipe.Default, scale);
            }
            overlay.PresentBitmap(bmp);

            PositionHitWindow(metrics, screenPos, pad);
        }
    }

    /// <summary>The buttons live on the invisible clickable window, positioned in screen
    /// coordinates over the pill's button band.</summary>
    private void PositionHitWindow(MusicStrip.Metrics metrics, Interop.POINT screenPos, int pad)
    {
        if (_hitWindow is null)
            _hitWindow = new RecordHitWindow(SendMediaCommand);

        int ox = screenPos.X + pad;
        int oy = screenPos.Y + pad;
        var band = new Rectangle(ox + metrics.PrevBox.X, oy,
            metrics.NextBox.Right - metrics.PrevBox.X, metrics.Size.Height);
        var buttons = new Rectangle[]
        {
            new(ox + metrics.PrevBox.X, oy + metrics.PrevBox.Y, metrics.PrevBox.Width, metrics.PrevBox.Height),
            new(ox + metrics.PlayBox.X, oy + metrics.PlayBox.Y, metrics.PlayBox.Width, metrics.PlayBox.Height),
            new(ox + metrics.NextBox.X, oy + metrics.NextBox.Y, metrics.NextBox.Width, metrics.NextBox.Height),
        };
        _hitWindow.Reposition(band, buttons);
    }

    private void SendMediaCommand(int index)
    {
        if (_disposed) return;
        var command = index switch
        {
            0 => NowPlayingSource.MediaCommand.Previous,
            1 => NowPlayingSource.MediaCommand.PlayPause,
            _ => NowPlayingSource.MediaCommand.Next,
        };
        _source.SendCommand(command);
    }

    private float ThemeScale => _style.Size * (float)_dpiScale * (_config.Disc.Size / 112f);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _ticking = false;
            _source.Changed -= OnSourceChanged;
            _timer.Dispose();
            _hitWindow?.Dispose();
            _hitWindow = null;
            try { _host.RemoveOverlay(OverlayKey); } catch { /* host may already be gone */ }
        }
    }
}
