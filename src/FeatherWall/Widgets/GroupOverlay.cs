using System.Drawing;
using System.Drawing.Imaging;
using Timer = System.Threading.Timer;
using FeatherWall.Common;
using FeatherWall.Config;
using FeatherWall.Desktop;
using FeatherWall.Rendering;
using FeatherWall.Widgets.Themes;

namespace FeatherWall.Widgets;

/// <summary>Orbit, Poster and Glass dock on the desktop: the theme's panels, each an ordered layer
/// group of a static layer and, for the panel holding the record, a face layer above it. Only that
/// small face is re-presented while the record turns, at the same 15 fps as the classic record —
/// the compositor-driven spin was measured and lost on GPU (docs/benchmark.md, 2026-11-10).
///
/// It replaces the four per-widget overlays while a group theme is active, and keeps their rules:
/// it repaints only on the clock's tick, a source event or a resume; it goes silent with the
/// display; and the record turns only while something plays on an uncovered, lit desktop with
/// Disc.Rotate set — the same <see cref="RecordSpin.ShouldTurn"/> gate.</summary>
public sealed class GroupOverlay : IDisposable
{
    private const string KeyPrefix = "group:";

    private sealed class PanelLayers
    {
        public required CompositionLayerGroup Group;
        public required CompositionSurface Static;
        public CompositionSurface? Face;
        public required Size Size;
        public required Rectangle FaceBox;
        /// <summary>What was last presented, so an unchanged panel is not re-shadowed each tick.</summary>
        public byte[]? Content;
    }

    private readonly CompositionHost _host;
    private readonly IGroupTheme _theme;
    private readonly AppConfig _config;
    private readonly MonitorInfo _monitor;
    private readonly NowPlayingSource? _nowPlaying;
    private readonly BatterySource? _battery;
    private readonly float _dpi;
    private readonly WidgetStyle _style;
    private readonly Timer _clockTimer;
    private readonly Timer _spinTimer;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, PanelLayers> _panels = [];

    private bool _disposed;
    private bool _suspended;
    private bool _covered;

    private Bitmap? _face;
    private string? _faceTrackId;
    private bool _faceHasArt;
    private (string? Track, bool Art, bool Playing, int Side)? _presentedFace;
    private CompositionSurface? _faceLayer;
    private float _turns;
    private bool _ticking;
    private bool _hasFace;

    public GroupOverlay(CompositionHost host, IGroupTheme theme, AppConfig config, MonitorInfo monitor,
                        NowPlayingSource? nowPlaying, BatterySource? battery, double dpiScale, WidgetStyle style)
    {
        _host = host;
        _theme = theme;
        _config = config;
        _monitor = monitor;
        _nowPlaying = nowPlaying;
        _battery = battery;
        _dpi = (float)(dpiScale > 0 ? dpiScale : 1.0);
        _style = style;
        _clockTimer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        _spinTimer = new Timer(_ => SpinTick(), null, Timeout.Infinite, Timeout.Infinite);

        if (_nowPlaying is not null) _nowPlaying.Changed += OnSourceChanged;
        if (_battery is not null) _battery.Changed += OnSourceChanged;
        try
        {
            Refresh();
        }
        catch
        {
            // The sources outlive this overlay; a throw here would leave it subscribed forever.
            Dispose();
            throw;
        }
    }

    public void Refresh()
    {
        Render();
        UpdateSpin();
        Arm();
    }

    private void OnSourceChanged()
    {
        try
        {
            Render();
            UpdateSpin();
        }
        catch (Exception ex)
        {
            Log.Error("Group widget render failed", ex);
        }
    }

    public void SetSuspended(bool suspended)
    {
        lock (_sync)
        {
            if (_disposed || _suspended == suspended) return;
            _suspended = suspended;
            if (suspended)
                try { _clockTimer.Change(Timeout.Infinite, Timeout.Infinite); } catch (ObjectDisposedException) { }
        }
        UpdateSpin();
        if (!suspended) Refresh();
    }

    public void SetCovered(bool covered)
    {
        lock (_sync)
        {
            if (_disposed || _covered == covered) return;
            _covered = covered;
        }
        UpdateSpin();
    }

    // ---- the clock's tick ---------------------------------------------------------------------

    private void Arm()
    {
        lock (_sync)
        {
            if (_disposed || _suspended || !_config.Clock.Enabled) return;
            try { _clockTimer.Change(ClockLayout.MillisecondsToNextTick(DateTime.Now, _config.Clock.ShowSeconds), Timeout.Infinite); }
            catch (ObjectDisposedException) { }
        }
    }

    private void Tick()
    {
        // UpdateSpin as well: a tick can rebuild a panel (a new size), which drops the spin, and
        // nothing else would start it again until the next track or cover change.
        try { Render(); UpdateSpin(); }
        catch (Exception ex) { Log.Error("Group widget tick failed", ex); }
        finally { Arm(); }
    }

    // ---- painting -----------------------------------------------------------------------------

    private void Render()
    {
        lock (_sync)
        {
            if (_disposed || _suspended) return;

            var disc = _config.Info.Disc;
            var halo = _config.Info.Halo;
            var track = _nowPlaying is null ? null : GroupThemes.TrackFor(disc, true, _nowPlaying.Current);
            var battery = _battery is null ? null : GroupThemes.BatteryFor(halo, true, _battery.Current);
            var work = new Rectangle(_monitor.WorkArea.Left - _monitor.Bounds.Left, _monitor.WorkArea.Top - _monitor.Bounds.Top,
                                     _monitor.WorkArea.Right - _monitor.WorkArea.Left, _monitor.WorkArea.Bottom - _monitor.WorkArea.Top);
            float scale = _style.Size * _dpi;
            var input = new GroupInput(DateTime.Now, _config.Clock, disc, halo, track, battery, _style.Accent,
                                       scale, _dpi, _config.Widgets.Position, work, _config.Info.MaxCharacters);

            var panels = _theme.Layout(input);
            foreach (var gone in _panels.Keys.Except(panels.Select(p => p.Key)).ToList())
                DropPanel(gone);

            _hasFace = false;
            foreach (var panel in panels)
            {
                var layers = EnsurePanel(panel);
                layers.Group.SetOffset(panel.Bounds.X, panel.Bounds.Y);

                using var bmp = new Bitmap(panel.Bounds.Width, panel.Bounds.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(bmp)) panel.Paint(g);
                // A clock tick changes only the clock: the other panels paint the same pixels, so
                // their shadow and present (the expensive part) are skipped.
                var content = PanelContent.Hash(bmp);
                if (layers.Content is null || !content.AsSpan().SequenceEqual(layers.Content))
                {
                    BakedShadow.DrawBehind(bmp, ShadowRecipe.Default, scale, panel.FaceBox.IsEmpty ? null : panel.FaceBox);
                    layers.Static.PresentBitmap(bmp);
                    layers.Content = content;
                }

                if (layers.Face is not null && track is { } t)
                {
                    _hasFace = true;
                    PresentFace(layers.Face, t, panel.FaceBox.Width);
                }
            }

            if (!_hasFace) DropFace();
        }
    }

    /// <summary>Builds or reuses a panel's layer group. A new size or a moved record rebuilds it,
    /// carrying the record's angle over so it does not snap back. Caller holds the lock.</summary>
    private PanelLayers EnsurePanel(GroupPanel panel)
    {
        if (_panels.TryGetValue(panel.Key, out var existing)
            && existing.Size == panel.Bounds.Size && existing.FaceBox == panel.FaceBox
            && _host.GetOverlayGroup(KeyPrefix + panel.Key) is not null)
            return existing;

        if (existing is not null) DropPanel(panel.Key);

        var group = _host.CreateOverlayGroup(KeyPrefix + panel.Key, panel.Bounds.X, panel.Bounds.Y);
        var layers = new PanelLayers
        {
            Group = group,
            Static = group.AddLayer(panel.Bounds.Width, panel.Bounds.Height, 0, 0),
            Size = panel.Bounds.Size,
            FaceBox = panel.FaceBox,
        };
        if (!panel.FaceBox.IsEmpty)
        {
            // Above the static layer: the dock's capsule and the rings sit under the record.
            layers.Face = group.AddLayer(panel.FaceBox.Width, panel.FaceBox.Height, panel.FaceBox.X, panel.FaceBox.Y);
            DropFace();
            _faceLayer = layers.Face;
        }
        _panels[panel.Key] = layers;
        return layers;
    }

    private void DropPanel(string key)
    {
        if (!_panels.Remove(key, out var layers)) return;
        if (ReferenceEquals(layers.Face, _faceLayer)) DropFace();
        try { _host.RemoveOverlay(KeyPrefix + key); } catch { /* host may already be gone */ }
    }

    /// <summary>Forgets the face layer and stops its timer; the angle stays in _turns, so a rebuilt
    /// record carries on from where it was. Caller holds the lock.</summary>
    private void DropFace()
    {
        _ticking = false;
        _faceLayer = null;
        _presentedFace = null;
        try { _spinTimer.Change(Timeout.Infinite, Timeout.Infinite); } catch (ObjectDisposedException) { }
    }

    /// <summary>Presented only when what the face shows changes; the compositor turns it between.</summary>
    private void PresentFace(CompositionSurface layer, NowPlayingReading track, int side)
    {
        if (side <= 0) return;
        var art = _nowPlaying?.Art;
        bool stale = _face is null || _faceTrackId != track.TrackId || _face.Width != side
                     || (!_faceHasArt && art is not null);
        if (stale)
        {
            _face?.Dispose();
            _face = DiscRenderer.RenderFace(side, art, _style.Accent);
            _faceTrackId = track.TrackId;
            _faceHasArt = art is not null;
        }

        var key = (_faceTrackId, _faceHasArt, track.IsPlaying, side);
        if (_presentedFace == key && _ticking) return;   // the spin timer is presenting it
        PaintFace(layer, track.IsPlaying);
        _presentedFace = key;
    }

    private void PaintFace(CompositionSurface layer, bool playing)
    {
        if (_face is null) return;
        using var bmp = new Bitmap(_face.Width, _face.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
            DiscRenderer.PaintFace(g, new Rectangle(0, 0, bmp.Width, bmp.Height), _face,
                                   _turns, playing);
        layer.PresentBitmap(bmp);
    }

    // ---- the record's turn ---------------------------------------------------------------------

    private void UpdateSpin()
    {
        lock (_sync)
        {
            bool playing = _nowPlaying?.Current.IsPlaying ?? false;
            bool shouldTurn = RecordSpin.ShouldTurn(_disposed, _suspended, _covered, _config.Info.Disc.Rotate,
                                                    playing, _hasFace && _faceLayer is not null);
            if (shouldTurn == _ticking) return;
            _ticking = shouldTurn;
            Log.Info(shouldTurn ? "Group: record turning" : "Group: record still");

            try { _spinTimer.Change(shouldTurn ? 0 : Timeout.Infinite, shouldTurn ? 1000 / RecordSpin.TimerFramesPerSecond : Timeout.Infinite); }
            catch (ObjectDisposedException) { }
        }
    }

    private void SpinTick()
    {
        try
        {
            lock (_sync)
            {
                if (_disposed || !_ticking || _faceLayer is null) return;
                _turns = (_turns + RecordSpin.DegreesPerSecond / 360f / RecordSpin.TimerFramesPerSecond) % 1f;
                PaintFace(_faceLayer, true);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Group record tick failed", ex);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            if (_nowPlaying is not null) _nowPlaying.Changed -= OnSourceChanged;
            if (_battery is not null) _battery.Changed -= OnSourceChanged;
            _clockTimer.Dispose();
            _spinTimer.Dispose();
            DropFace();
            foreach (var key in _panels.Keys.ToList()) DropPanel(key);
            _face?.Dispose();
            _face = null;
        }
    }
}
