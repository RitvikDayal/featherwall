using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PrismOS.Tray;

/// <summary>A scaled preview of the wallpaper on one display.
///
/// For an image it is the image; for a video it is the frame the engine captured when playback
/// started, which is the only still there is. Either way it is what the display is showing now,
/// which is the point: the settings panel used to say nothing about what was on the desktop.
///
/// The bitmap is downscaled on load rather than drawn at full size. A 4000x2250 wallpaper is
/// 36 MB of GDI+ memory for a preview 444 pixels wide, and the settings panel is opened and
/// closed all day.</summary>
public sealed class WallpaperPreview : Control
{
    private const int MaxPreviewPixels = 640;

    private Bitmap? _bitmap;
    private string? _path;
    private string _placeholder = "No wallpaper set";
    private string _caption = "";

    public WallpaperPreview()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                  ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Colors.Preview;
        ForeColor = Theme.Colors.Text;
        Size = new Size(444, 249);
    }

    /// <summary>Shown when there is no wallpaper to preview.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Placeholder
    {
        get => _placeholder;
        set { _placeholder = value; Invalidate(); }
    }

    /// <summary>The file name of the wallpaper, drawn under the image.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Caption
    {
        get => _caption;
        set { _caption = value; Invalidate(); }
    }

    /// <summary>Points at a new image. Null clears the preview. A path that cannot be read is
    /// cleared rather than throwing: a wallpaper may be on a drive that is not plugged in.</summary>
    public void SetSource(string? path)
    {
        if (string.Equals(_path, path, StringComparison.OrdinalIgnoreCase)) return;
        _path = path;
        Load();
        Invalidate();
    }

    private void Load()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path)) return;
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
        var g = e.Graphics;
        g.Clear(BackColor);

        // The caption band sits under the image, so the image area is the control minus it.
        int captionHeight = _caption.Length > 0 ? 22 : 0;
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
            _bitmap?.Dispose();
            _bitmap = null;
        }
        base.Dispose(disposing);
    }
}
