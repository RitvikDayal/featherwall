using System.Drawing.Drawing2D;
using System.Drawing.Text;
using PrismOS.Common;
using PrismOS.Config;
using PrismOS.Rendering;
using PrismOS.Widgets.Themes;

namespace PrismOS.Tray;

/// <summary>A live preview of the wallpaper on one display.
///
/// For an image it is the image, downscaled on load. For a video it is the video: the same
/// WinRT MediaPlayer + DirectComposition pipeline the desktop wallpaper uses, hosted on this
/// control's own handle, decoded and presented continuously and muted. A still frame of a video
/// in a "what will I see" preview is a lie about motion, and the frame is misleading for
/// anything the user would call a wallpaper.
///
/// The caption is unusual here: plain WinForms paint would be covered by the composition visual
/// tree, so caption text is presented as an overlay surface above the video, exactly like the
/// clock and the info widget are on the desktop.</summary>
public sealed class WallpaperPreview : Control
{
    private const int MaxPreviewPixels = 640;
    private const int CaptionHeight = 22;
    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".mov", ".avi", ".wmv", ".mkv", ".webm" };

    private Bitmap? _bitmap;
    private string? _path;
    private string _placeholder = "No wallpaper set";
    private string _caption = "";

    // DirectComposition video pane state.
    private CompositionHost? _host;
    private VideoRenderer? _renderer;
    private bool _videoActive;

    public WallpaperPreview()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                  ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Black;
        ForeColor = Theme.Colors.Text;
        Size = new Size(444, 249);
    }

    /// <summary>Shown when there is no wallpaper to preview.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Placeholder
    {
        get => _placeholder;
        set { _placeholder = value; if (!_videoActive) Invalidate(); }
    }

    /// <summary>The file name of the wallpaper, drawn along the bottom edge.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Caption
    {
        get => _caption;
        set
        {
            if (_caption == value) return;
            _caption = value;
            if (_videoActive) PaintCaption(); else Invalidate();
        }
    }

    /// <summary>Points at a new image or video. Null clears the preview. A path that cannot be read
    /// is cleared rather than throwing: a wallpaper may be on a drive that is not plugged in.</summary>
    public void SetSource(string? path)
    {
        if (string.Equals(_path, path, StringComparison.OrdinalIgnoreCase))
        {
            // Same file, different caption: the caption lives on the overlay, which is why this
            // needs handling even without a source change.
            if (_videoActive) PaintCaption();
            return;
        }
        _path = path;
        if (path is null) { ShowBitmapPreview(null); return; }
        if (IsVideoPath(path)) { StartVideo(path); return; }
        StopVideo();
        Load();
        Invalidate();
    }

    /// <summary>True for extensions the video pipeline handles. Public so the settings panel
    /// can pick a still frame instead when the live preview is off.</summary>
    public static bool IsVideoPath(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path));

    // ---- video mode -------------------------------------------------------------------------

    private void StartVideo(string path)
    {
        StopVideo();
        if (Width <= 0 || Height <= 0 || !IsHandleCreated)
        {
            // The control is not laid out yet; wait for a real size/handle.
            _placeholder = "Loading preview…";
            return;
        }
        try
        {
            _host = new CompositionHost(Handle);
            _renderer = new VideoRenderer(_host, Width, Height, FitMode.Fit, muted: true, volume: 0.0);
            _renderer.PlaybackFailed += OnPlaybackFailed;
            _renderer.Load(path);
            _videoActive = true;
            Log.Info($"Wallpaper preview: video pane hosting {path}");
            PaintCaption();
            if (!Visible) _renderer.Pause();
        }
        catch (Exception ex)
        {
            Log.Error("Wallpaper preview could not start the video", ex);
            StopVideo();
            _videoActive = false;
            _path = null;
            _placeholder = "Could not start this video";
            Invalidate();
        }
    }

    /// <summary>The video failed twice. Stops the pane and says so in the placeholder instead of
    /// a black hole in the panel.</summary>
    private void OnPlaybackFailed(string path, string detail)
    {
        if (IsDisposed) return;
        try
        {
            BeginInvoke((MethodInvoker)(() =>
            {
                if (IsDisposed) return;
                StopVideo();
                _videoActive = false;
                _path = null;
                _placeholder = $"Could not play {Path.GetFileName(path)}: {detail}";
                Log.Warn($"Wallpaper preview fallback for {Path.GetFileName(path)}: {detail}");
                Invalidate();
            }));
        }
        catch { /* the panel went away mid-notification */ }
    }

    private void StopVideo()
    {
        _renderer?.Dispose();
        _renderer = null;
        _host?.Dispose();
        _host = null;
        if (_videoActive) VideoRenderer.ReclaimMediaPipeline();
        _videoActive = false;
    }

    /// <summary>Presents the caption file-name line as an overlay above the video — plain paint is
    /// invisible underneath the composition tree.</summary>
    private void PaintCaption()
    {
        if (_host is null) return;
        if (_caption.Length == 0)
        {
            _host.RemoveOverlay("preview-caption");
            return;
        }
        var overlay = _host.GetOverlay("preview-caption")
                      ?? _host.CreateOverlay("preview-caption", Width, CaptionHeight, 0, Height - CaptionHeight);
        using var bmp = new Bitmap(Width, CaptionHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            using var band = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
            g.FillRectangle(band, 0, 0, Width, CaptionHeight);
            using var text = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
            int x = 8;
            g.DrawString(_caption, Font, text, x, 3, ThemeFonts.Tight);
        }
        overlay.PresentBitmap(bmp);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // If a video was requested before the handle or size existed, start it now.
        if (!_videoActive && _path is not null && IsVideoPath(_path) && Width > 0 && Height > 0)
            StartVideo(_path);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_path is null || !IsVideoPath(_path)) return;
        if (Width <= 0 || Height <= 0) return;
        // Start if we were waiting for layout, or recreate on size drift.
        StartVideo(_path);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_renderer is null) return;
        if (Visible) _renderer.Resume(); else _renderer.Pause();
    }

    // ---- image mode ------------------------------------------------------------------------

    private void ShowBitmapPreview(string? path)
    {
        StopVideo();
        Load();
        Invalidate();
    }

    private void Load()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path)) return;
        if (IsVideoPath(_path)) return; // handled by the video pane
        try
        {
            using var stream = File.OpenRead(_path);
            using var decoded = Image.FromStream(stream);
            _bitmap = Downscale(decoded, MaxPreviewPixels);
        }
        catch (Exception ex)
        {
            Common.Log.Warn($"Wallpaper preview unavailable for {_path}: {ex.Message}");
            _bitmap = null;
        }
    }

    /// <summary>A copy of the image no wider than <paramref name="maxPixels"/>, so a 8K
    /// wallpaper does not cost a full-resolution GDI+ bitmap to fill a small box.</summary>
    private static Bitmap Downscale(Image source, int maxPixels)
    {
        int width = source.Width;
        int height = source.Height;
        if (width > maxPixels)
        {
            height = Math.Max(1, (int)Math.Round(height * (maxPixels / (double)width)));
            width = maxPixels;
        }
        var scaled = new Bitmap(Math.Max(1, width), Math.Max(1, height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(scaled);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, scaled.Width, scaled.Height));
        return scaled;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // In video mode the composition tree owns the surface, including the caption; WinForms
        // paint is hidden underneath it, and painting would only fight the decoder for the hwnd.
        if (_videoActive) { e.Graphics.Clear(BackColor); return; }
        var g = e.Graphics;
        g.Clear(BackColor);

        // The caption band sits under the image, so the image area is the control minus it.
        int captionHeight = _caption.Length > 0 ? CaptionHeight : 0;
        var imageArea = new Rectangle(0, 0, Width, Height - captionHeight);

        if (_bitmap is null)
        {
            using var text = new SolidBrush(Theme.Colors.Subtle);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(_placeholder, Font, text, imageArea, format);
        }
        else
        {
            // Fit, never crop: a preview that hid the edges of the wallpaper would not be
            // showing the wallpaper.
            double scale = Math.Min((double)imageArea.Width / _bitmap.Width, (double)imageArea.Height / _bitmap.Height);
            int w = Math.Max(1, (int)Math.Round(_bitmap.Width * scale));
            int h = Math.Max(1, (int)Math.Round(_bitmap.Height * scale));
            var dest = new Rectangle(imageArea.X + (imageArea.Width - w) / 2, imageArea.Y + (imageArea.Height - h) / 2, w, h);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_bitmap, dest);
        }

        if (captionHeight > 0)
        {
            using var back = new SolidBrush(Theme.Colors.Card);
            g.FillRectangle(back, new Rectangle(0, Height - captionHeight, Width, captionHeight));
            using var text = new SolidBrush(Theme.Colors.Text);
            using var format = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisPath,
            };
            g.DrawString(_caption, Font, text, new Rectangle(8, Height - captionHeight, Width - 16, captionHeight), format);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopVideo();
            _bitmap?.Dispose();
            _bitmap = null;
        }
        base.Dispose(disposing);
    }
}
