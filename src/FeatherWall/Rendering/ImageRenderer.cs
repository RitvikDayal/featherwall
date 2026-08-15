using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using FeatherWall.Config;

namespace FeatherWall.Rendering;

/// <summary>Static images and animated GIFs. The scaled frame is drawn once onto the
/// composition surface (via Direct2D) — zero ongoing CPU/GPU for static images.</summary>
public sealed class ImageRenderer : IWallpaperRenderer
{
    private readonly int _width;
    private readonly int _height;
    private readonly FitMode _fit;
    private readonly Lock _sync = new();

    private readonly CompositionSurface _surface;
    private readonly string? _staticFramePath;
    private readonly Action? _onStaticFrame;
    private Bitmap? _canvas;            // reused draw target sized to the window
    private Image? _animated;           // original image when it is an animated GIF
    private FitRect _rect;
    private bool _animating;
    private bool _disposed;

    public ImageRenderer(CompositionHost host, int width, int height, FitMode fit,
        string? staticFramePath = null, Action? onStaticFrame = null)
    {
        _width = width;
        _height = height;
        _fit = fit;
        _staticFramePath = staticFramePath;
        _onStaticFrame = onStaticFrame;
        _surface = host.CreateContent(width, height);
    }

    public void Load(string path)
    {
        var image = Image.FromFile(path);
        lock (_sync)
        {
            _canvas = new Bitmap(_width, _height, PixelFormat.Format32bppPArgb);
            _rect = FitCalculator.Compute(image.Width, image.Height, _width, _height, _fit);

            if (ImageAnimator.CanAnimate(image))
            {
                _animated = image;
                ImageAnimator.Animate(image, OnFrameChanged);
                _animating = true;
                DrawAndPresent(image, highQuality: false);
                CaptureStatic();
                return;
            }

            using (image)
            {
                DrawAndPresent(image, highQuality: true);
            }
            CaptureStatic();
        }
    }

    private void CaptureStatic()
    {
        if (_staticFramePath is null || _canvas is null) return;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_staticFramePath)!);
            // _canvas is already the monitor-sized composed frame (fit applied) — save directly.
            using var copy = new Bitmap(_canvas);
            copy.Save(_staticFramePath, ImageFormat.Png);
            _onStaticFrame?.Invoke();
        }
        catch (Exception ex)
        {
            Common.Log.Error("Static image capture failed", ex);
        }
    }

    private void OnFrameChanged(object? sender, EventArgs e)
    {
        try
        {
            lock (_sync)
            {
                if (_disposed || _animated is null) return;
                ImageAnimator.UpdateFrames(_animated);
                DrawAndPresent(_animated, highQuality: false);
            }
        }
        catch (Exception ex)
        {
            // Runs on ImageAnimator's thread — an exception here would kill the process.
            Common.Log.Error("GIF frame update failed", ex);
        }
    }

    private void DrawAndPresent(Image image, bool highQuality)
    {
        if (_canvas is null) return;
        using (var g = Graphics.FromImage(_canvas))
        {
            g.Clear(Color.Black);
            if (highQuality)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            }
            g.DrawImage(image, new Rectangle(_rect.X, _rect.Y, _rect.Width, _rect.Height));
        }
        try
        {
            _surface.PresentBitmap(_canvas);
        }
        catch (Exception ex)
        {
            Common.Log.Error("Image present failed", ex);
        }
    }

    public void Paint(IntPtr hdc) { /* composition-presented */ }

    public void Pause()
    {
        lock (_sync)
        {
            if (_animated is not null && _animating)
            {
                ImageAnimator.StopAnimate(_animated, OnFrameChanged);
                _animating = false;
            }
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (_animated is not null && !_animating)
            {
                ImageAnimator.Animate(_animated, OnFrameChanged);
                _animating = true;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            if (_animated is not null)
            {
                if (_animating) ImageAnimator.StopAnimate(_animated, OnFrameChanged);
                _animated.Dispose();
                _animated = null;
            }
            _canvas?.Dispose();
            _canvas = null;
            // The content surface and host belong to the WallpaperWindow.
        }
    }
}
