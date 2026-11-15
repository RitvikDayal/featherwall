using System.Drawing.Drawing2D;
using PrismOS.Common;

namespace PrismOS.Tray;

/// <summary>A live preview of the wallpaper on one display.
///
/// An image shows the image, downscaled on load. A video plays through a software Media
/// Foundation decode (see <see cref="CpuVideoDecoder"/>) and is drawn with GDI+ — no D3D device
/// is created, so the NVIDIA App overlay is not triggered. It is muted and loops.
///
/// The caption (the wallpaper's file name) is drawn along the bottom edge with a translucent
/// band, which is also what keeps it readable over bright frames.</summary>
public sealed class WallpaperPreview : Control
{
    private const int MaxPreviewPixels = 640;
    private const int CaptionHeight = 22;
    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".mov", ".avi", ".wmv", ".mkv", ".webm" };

    private Bitmap? _bitmap;
    private CpuVideoDecoder? _decoder;
    private string? _path;
    private string _placeholder = "No wallpaper set";
    private string _caption = "";

    public WallpaperPreview()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                  ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Black;
        ForeColor = Theme.Colors.Text;
        Size = new Size(444, 249);
    }

    /// <summary>True for extensions the video pipeline handles. Public so the settings panel
    /// can pick a still frame instead when the live preview is off.</summary>
    public static bool IsVideoPath(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path));

    /// <summary>Shown when there is no wallpaper to preview.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Placeholder
    {
        get => _placeholder;
        set { _placeholder = value; Invalidate(); }
    }

    /// <summary>The file name of the wallpaper, drawn along the bottom edge.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Caption
    {
        get => _caption;
        set { _caption = value; Invalidate(); }
    }

    /// <summary>Points at a new image or video. Null clears the preview. A path that cannot be read
    /// is cleared rather than throwing: a wallpaper may be on a drive that is not plugged in.</summary>
    public void SetSource(string? path)
    {
        if (string.Equals(_path, path, StringComparison.OrdinalIgnoreCase)) return;
        _path = path;

        StopDecoder();
        _bitmap?.Dispose();
        _bitmap = null;

        if (path is null)
        {
            _placeholder = "No wallpaper on this display";
            Invalidate();
            return;
        }
        if (!File.Exists(path))
        {
            _placeholder = $"Not found: {Path.GetFileName(path)}";
            Invalidate();
            return;
        }
        if (IsVideoPath(path))
        {
            _decoder = new CpuVideoDecoder(path);
            _decoder.FrameReady += OnFrameReady;
            if (Visible) _decoder.Start();
            _placeholder = "Loading preview…";
        }
        else
        {
            LoadImage(path);
        }
        Invalidate();
    }

    private void StopDecoder()
    {
        if (_decoder is null) return;
        try { _decoder.FrameReady -= OnFrameReady; } catch { }
        try { _decoder.Dispose(); } catch (Exception ex) { Log.Warn($"Decoder disposal failed: {ex.Message}"); }
        _decoder = null;
    }

    private void OnFrameReady()
    {
        if (IsDisposed) return;
        try
        {
            if (!IsHandleCreated) return;
            BeginInvoke((MethodInvoker)(() => Invalidate()));
        }
        catch { /* the panel went away mid-notification */ }
    }

    private void LoadImage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var decoded = Image.FromStream(stream);
            _bitmap = Downscale(decoded, MaxPreviewPixels);
        }
        catch (Exception ex)
        {
            Common.Log.Warn($"Wallpaper preview unavailable for {path}: {ex.Message}");
            _bitmap = null;
            _placeholder = $"Could not read {Path.GetFileName(path)}";
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

    private void DrawImage(Graphics g, Image image, Rectangle area)
    {
        double scale = Math.Min((double)area.Width / image.Width, (double)area.Height / image.Height);
        int w = Math.Max(1, (int)Math.Round(image.Width * scale));
        int h = Math.Max(1, (int)Math.Round(image.Height * scale));
        var dest = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(image, dest);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_decoder is null) return;
        if (Visible) { _decoder.Start(); _decoder.Resume(); } else _decoder.Pause();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        int captionHeight = _caption.Length > 0 ? CaptionHeight : 0;
        var imageArea = new Rectangle(0, 0, Width, Height - captionHeight);

        var frame = _decoder?.GetFrame();
        var image = (Image?)frame ?? _bitmap;
        string? error = _decoder?.Error;

        try
        {
            if (image is not null)
            {
                DrawImage(g, image, imageArea);
            }
            else
            {
                using var text = new SolidBrush(Theme.Colors.Subtle);
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                };
                g.DrawString(error is not null ? $"Could not play: {error}" : _placeholder, Font, text, imageArea, format);
            }
        }
        finally
        {
            frame?.Dispose(); // the decoder hands out clones; the still bitmap belongs to us
        }

        if (captionHeight > 0)
        {
            using var back = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
            g.FillRectangle(back, new Rectangle(0, Height - captionHeight, Width, captionHeight));
            using var text = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
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
            StopDecoder();
            _bitmap?.Dispose();
            _bitmap = null;
        }
        base.Dispose(disposing);
    }
}
