using FeatherWall.Common;
using FeatherWall.Config;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace FeatherWall.Rendering;

/// <summary>Hardware-decoded looping video via WinRT MediaPlayer in frame-server mode:
/// each decoded frame is copied onto the wallpaper's composition surface and presented.
/// Zero timers — fully event-driven, so a paused video costs nothing.</summary>
public sealed class VideoRenderer : IWallpaperRenderer
{
    private readonly CompositionHost _hostComposition;
    private readonly int _width;
    private readonly int _height;
    private readonly FitMode _fit;
    private readonly Lock _sync = new();

    private CompositionSurface _surfaceHost;
    private IDirect3DSurface? _surface;

    private MediaPlayer? _player;
    private MediaSource? _source;
    private Windows.Foundation.Rect? _targetRect;
    private bool _retriedAfterFailure;
    private int _loadGeneration;
    private bool _disposed;
    private string _path = "";

    /// <summary>Raised when playback has failed and the one retry has also failed. Until
    /// 2026-08-20 a codec Windows cannot decode produced a log line and a black desktop, with
    /// nothing shown to the user. Carries (path, codecOrError) so the caller can name the fix.</summary>
    public event Action<string, string>? PlaybackFailed;

    private readonly string? _staticFramePath;
    private readonly Action? _onStaticFrame;
    private int _staticCropX;
    private int _staticCropY;
    private bool _staticCaptured;
    private int _frameCount;
    private bool _pauseDeferred;

    public VideoRenderer(CompositionHost host, int width, int height, FitMode fit, bool muted, double volume,
        string? staticFramePath = null, Action? onStaticFrame = null)
    {
        _hostComposition = host;
        _width = width;
        _height = height;
        _fit = fit;
        _staticFramePath = staticFramePath;
        _onStaticFrame = onStaticFrame;

        _surfaceHost = host.CreateContent(width, height);
        WrapBackBuffer();

        _player = new MediaPlayer
        {
            IsVideoFrameServerEnabled = true,
            IsLoopingEnabled = true,
            IsMuted = muted,
            Volume = volume,
            RealTimePlayback = true,
        };
        _player.CommandManager.IsEnabled = false; // keep media keys away from the wallpaper
        _player.MediaOpened += OnMediaOpened;
        _player.MediaFailed += OnMediaFailed;
        _player.MediaEnded += OnMediaEnded;
        _player.VideoFrameAvailable += OnVideoFrameAvailable;
    }

    private void WrapBackBuffer()
    {
        _surface?.Dispose();
        using var dxgiSurface = _surfaceHost.BackBuffer.QueryInterface<IDXGISurface>();
        _surface = Interop.Direct3DInterop.CreateSurfaceFromDxgi(dxgiSurface.NativePointer);
    }

    public bool IsMuted
    {
        get => _player?.IsMuted ?? true;
        set { if (_player is not null) _player.IsMuted = value; }
    }

    public double Volume
    {
        get => _player?.Volume ?? 0;
        set { if (_player is not null) _player.Volume = Math.Clamp(value, 0, 1); }
    }

    public void Load(string path)
    {
        if (_player is null) return;
        _path = path;
        // Identifies this selection for the delayed retry below. Without it, a retry armed for the
        // previous file lands a second later and reinstates a source that Load has already disposed
        // and replaced — either an error on a dead source or the new wallpaper silently reverting.
        Interlocked.Increment(ref _loadGeneration);
        _retriedAfterFailure = false; // a new file deserves its own retry
        var previous = _source;
        _source = MediaSource.CreateFromUri(new Uri(path));
        _player.Source = _source;
        previous?.Dispose(); // a re-Load would otherwise strand the old source
        _player.Play();
        Log.Info($"Video loaded: {path}");
    }

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        try
        {
            uint videoW = sender.PlaybackSession.NaturalVideoWidth;
            uint videoH = sender.PlaybackSession.NaturalVideoHeight;
            var fit = FitCalculator.Compute((int)videoW, (int)videoH, _width, _height, _fit);
            lock (_sync)
            {
                if (_disposed) return;
                if (fit is { X: 0, Y: 0 } && fit.Width == _width && fit.Height == _height)
                {
                    _targetRect = null;
                }
                else if (fit.X < 0 || fit.Y < 0)
                {
                    // Cover-crop: content surface sized to the scaled video, the window
                    // clips the overflow. (CopyFrameToVideoSurface mishandles rects that
                    // overflow the surface, so never pass those.)
                    _surface?.Dispose();
                    _surface = null;
                    _surfaceHost = _hostComposition.CreateContent(fit.Width, fit.Height, fit.X, fit.Y);
                    WrapBackBuffer();
                    _targetRect = null;
                    _staticCropX = -fit.X; // visible monitor region within the oversized surface
                    _staticCropY = -fit.Y;
                }
                else
                {
                    // Letterbox: draw into a centered sub-rect over a black clear.
                    _targetRect = new Windows.Foundation.Rect(fit.X, fit.Y, fit.Width, fit.Height);
                }
            }
            Log.Info($"Media opened {videoW}x{videoH}, fit: {fit}");
        }
        catch (Exception ex)
        {
            Log.Error("MediaOpened handler failed", ex);
        }
    }

    /// <summary>Loop safety net: IsLoopingEnabled handles most files internally (MediaEnded
    /// never fires then), but frame-server playback of some sources ends without looping —
    /// so restart explicitly when it does.</summary>
    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        try
        {
            if (_disposed) return;
            sender.PlaybackSession.Position = TimeSpan.Zero;
            sender.Play();
        }
        catch (Exception ex)
        {
            Log.Error("Video loop restart failed", ex);
        }
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        Log.Error($"Video playback failed: {args.Error} / {args.ErrorMessage} (0x{args.ExtendedErrorCode.HResult:X8}). " +
                  "If this is HEVC/VP9/AV1, the matching (free) codec extension from the Microsoft Store may be missing.");
        if (!_retriedAfterFailure)
        {
            _retriedAfterFailure = true;
            var source = sender.Source;
            int failedGeneration = Volatile.Read(ref _loadGeneration);
            _ = Task.Delay(1000).ContinueWith(_ =>
            {
                try
                {
                    if (_disposed || _player is null) return;
                    if (failedGeneration != Volatile.Read(ref _loadGeneration)) return; // superseded
                    _player.Source = source;
                    _player.Play();
                }
                catch (Exception ex) { Log.Error("Video retry failed", ex); }
            });
            return;
        }

        // Second failure: the retry did not help, so stop logging into the void and say so.
        PlaybackFailed?.Invoke(_path, DescribeFailure(args));
    }

    /// <summary>Best available codec name. MediaPlayerFailedEventArgs does not carry the FourCC,
    /// so an unsupported-format error is reported as exactly that rather than guessed at — a
    /// wrong codec name in an error message is worse than an honest "unsupported".</summary>
    private static string DescribeFailure(MediaPlayerFailedEventArgs args) =>
        args.Error == MediaPlayerError.DecodingError || args.Error == MediaPlayerError.SourceNotSupported
            ? "unsupported or missing decoder"
            : $"{args.Error}";

    private void OnVideoFrameAvailable(MediaPlayer sender, object args)
    {
        lock (_sync)
        {
            if (_disposed || _surface is null) return;
            try
            {
                if (_targetRect is { } rect)
                {
                    _surfaceHost.ClearBlack();
                    sender.CopyFrameToVideoSurface(_surface, rect);
                }
                else
                {
                    sender.CopyFrameToVideoSurface(_surface);
                }

                // Snapshot a settled frame for the static desktop-switch fallback, before
                // Present (flip-model backbuffer is undefined afterward). Skip the first few
                // frames — the earliest can predate the video dimensions settling.
                if (!_staticCaptured && _staticFramePath is not null && ++_frameCount >= 3)
                {
                    _staticCaptured = true;
                    try
                    {
                        _surfaceHost.SaveRegionPng(_staticCropX, _staticCropY, _width, _height, _staticFramePath);
                        _onStaticFrame?.Invoke();
                    }
                    catch (Exception ex) { Log.Error("Static frame capture failed", ex); }

                    // Honor a pause that was deferred so this frame could be captured.
                    if (_pauseDeferred)
                    {
                        _pauseDeferred = false;
                        try { _player?.Pause(); } catch { }
                    }
                }

                _surfaceHost.Present();
            }
            catch (Exception ex)
            {
                Log.Error("Frame present failed", ex);
            }
        }
    }

    public void Pause()
    {
        // Defer pausing until the static desktop-switch frame is captured — otherwise a
        // wallpaper applied while a fullscreen app is active would never capture one.
        if (!_staticCaptured && _staticFramePath is not null)
        {
            _pauseDeferred = true;
            return;
        }
        _player?.Pause();
    }

    public void Resume()
    {
        _pauseDeferred = false;
        _player?.Play();
    }

    public void Paint(IntPtr hdc) { /* composition-presented; nothing to do on WM_PAINT */ }

    /// <summary>Releases the Media Foundation pipeline left behind by a disposed
    /// <see cref="MediaPlayer"/>.
    ///
    /// Disposing the player is not enough: every projected child object touched along the way
    /// (<c>PlaybackSession</c>, <c>CommandManager</c>) creates its own RCW holding a COM
    /// reference to the native player, and those RCWs have no Close/Dispose to call. The player
    /// therefore survives its own Dispose and its ~27 worker threads with it, until finalization
    /// runs. Measured directly: threads before=86, after Dispose=86, after a forced collect=56.
    ///
    /// Left alone this compounds — every display change, monitor hot-plug or explorer restart
    /// stranded ~27 threads and ~24 MB, taking a long-running instance past 500 MB and 129
    /// threads. Teardown happens only on those rare events, never per frame, so a blocking
    /// collect here is cheap and keeps the process flat.</summary>
    public static void ReclaimMediaPipeline()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            if (_player is not null)
            {
                _player.VideoFrameAvailable -= OnVideoFrameAvailable;
                _player.MediaOpened -= OnMediaOpened;
                _player.MediaFailed -= OnMediaFailed;
                _player.MediaEnded -= OnMediaEnded;
                try { _player.Pause(); } catch { }

                // Detach the source BEFORE disposing anything. Disposing a MediaSource while it
                // is still assigned leaves the Media Foundation pipeline referenced, and its
                // worker threads are never reclaimed: every re-apply (display change, explorer
                // restart, monitor hot-plug) stranded ~27 threads and ~24 MB, so a laptop that
                // docks a few times a day climbed past 500 MB. Measured, not theorised.
                try { _player.Source = null; } catch { }
                _source?.Dispose();
                _source = null;
                _player.Dispose();
                _player = null;
            }
            _surface?.Dispose();
            _surface = null;
            // The content surface and host belong to the WallpaperWindow.
        }
    }
}
