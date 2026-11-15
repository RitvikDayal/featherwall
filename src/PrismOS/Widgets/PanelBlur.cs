using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using PrismOS.Desktop;

namespace PrismOS.Widgets;

/// <summary>The blurred wallpaper that shows through card-bearing widgets (the pill, the
/// capsule, the Tiles cards). Downscaled to a fraction of the screen and blurred with a two-pass
/// separable box blur — the result sits behind translucent card fills and reads as frosted
/// glass. Re-derived only when the file changes; the monitor's blur is reused across every
/// repaint and every panel of that monitor.</summary>
public static class PanelBlur
{
    private const int MaxWidth = 240;

    /// <summary>One card region to frost: bounds in panel pixels, corner radius in the same.</summary>
    public readonly record struct Region(RectangleF Bounds, float Radius);

    private sealed class CacheEntry
    {
        public DateTime LastWriteUtc;
        public long Length;
        public Bitmap? Still;
    }

    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new();

    /// <summary>The monitor's blur, cached by file identity. Null when there is nothing to
    /// blur yet (the wallpaper is an image whose file vanished, or a video still not played).</summary>
    public static Bitmap? Load(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        var fi = new FileInfo(path);
        string key = fi.FullName;
        var slot = Cache.GetOrAdd(key, _ => new CacheEntry());
        if (slot.Still is null || slot.LastWriteUtc != fi.LastWriteTimeUtc || slot.Length != fi.Length)
        {
            slot.Still?.Dispose();
            slot.Still = CreateScaledAndBlurred(path, fi.LastWriteTimeUtc, fi.Length);
            if (slot.Still is null) return null;
            slot.LastWriteUtc = fi.LastWriteTimeUtc;
            slot.Length = fi.Length;
        }
        return slot.Still;
    }

    /// <summary>Draws the blurred wallpaper under the card regions of a panel, clipped to
    /// rounded rectangles so the corners stay round. Call before painting the panel's own
    /// (translucent) content.</summary>
    public static void ApplyUnderPanel(Graphics g, MonitorInfo monitor, string stillPath,
        Rectangle panelBoundsOnMonitor, IReadOnlyList<Region> regions)
    {
        var blurred = Load(stillPath);
        if (blurred is null) return;
        float dw = monitor.Bounds.Width, dh = monitor.Bounds.Height;
        if (dw <= 0 || dh <= 0) return;

        foreach (var region in regions)
        {
            using var path = RoundedRect(region.Bounds, region.Radius);
            g.SetClip(path, CombineMode.Replace);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            // The panel bitmap has panel-local coordinates; draw the monitor blur offset by
            // the panel's position on the monitor, scaled to monitor size.
            g.DrawImage(blurred, -panelBoundsOnMonitor.X, -panelBoundsOnMonitor.Y, dw, dh);
            g.ResetClip();
        }
    }

    private static Bitmap? CreateScaledAndBlurred(string path, DateTime lastWriteUtc, long length)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var decoded = Image.FromStream(stream, useEmbeddedColorManagement: true);
            int w = Math.Max(2, Math.Min(MaxWidth, decoded.Width));
            int h = Math.Max(2, (int)Math.Round(decoded.Height * (w / (double)decoded.Width)));
            var small = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(decoded, 0, 0, w, h);
            }
            BoxBlur(small, Math.Max(6, w / 16), passes: 2);
            return small;
        }
        catch (Exception ex)
        {
            Common.Log.Warn($"Panel blur unavailable for {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Two-pass separable box blur: sharp enough to recognise the wallpaper, soft
    /// enough to frost. Runs in place; the caller owns the bitmap.</summary>
    public static unsafe void BoxBlur(Bitmap bmp, int radius, int passes)
    {
        if (radius < 1) return;
        int w = bmp.Width, h = bmp.Height;
        var bits = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            int stride = bits.Stride;
            var buffer = new byte[(Math.Max(w, h) + 1) * 4];
            for (int pass = 0; pass < passes; pass++)
            {
                // Horizontal: for each row, a running sum over the window [x-radius, x+radius].
                for (int y = 0; y < h; y++) HorizontalPass((byte*)bits.Scan0 + y * stride, w, radius, buffer);
                // Vertical: for each column, a running sum down the rows — strided access, same running-sum shape.
                VerticalPass((byte*)bits.Scan0, h, stride, radius, buffer);
            }
        }
        finally
        {
            bmp.UnlockBits(bits);
        }
    }

    private static unsafe void HorizontalPass(byte* row, int width, int radius, byte[] buffer)
    {
        int window = radius * 2 + 1;
        uint sumB = 0, sumG = 0, sumR = 0, sumA = 0;
        int left = -radius, right = radius;
        for (int x = left; x <= right; x++)
        {
            int cx = Math.Clamp(x, 0, width - 1) * 4;
            sumB += row[cx]; sumG += row[cx + 1]; sumR += row[cx + 2]; sumA += row[cx + 3];
        }
        for (int x = 0; x < width; x++)
        {
            buffer[x * 4] = (byte)(sumB / window);
            buffer[x * 4 + 1] = (byte)(sumG / window);
            buffer[x * 4 + 2] = (byte)(sumR / window);
            buffer[x * 4 + 3] = (byte)(sumA / window);
            int add = Math.Clamp(x + radius + 1, 0, width - 1) * 4;
            int sub = Math.Clamp(x - radius, 0, width - 1) * 4;
            sumB += (uint)(row[add] - row[sub]); sumG += (uint)(row[add + 1] - row[sub + 1]);
            sumR += (uint)(row[add + 2] - row[sub + 2]); sumA += (uint)(row[add + 3] - row[sub + 3]);
        }
        fixed (byte* src = buffer) Buffer.MemoryCopy(src, row, width * 4L, width * 4L);
    }

    private static unsafe void VerticalPass(byte* basePtr, int height, int stride, int radius, byte[] buffer)
    {
        int window = radius * 2 + 1;
        int rowStride = stride;

        // Column by column; each pass uses its own sliding window.
        for (int x = 0; x < rowStride / 4; x++)
        {
            int offset = x * 4;
            uint sumB = 0, sumG = 0, sumR = 0, sumA = 0;
            for (int y = -radius; y <= radius; y++)
            {
                int cy = Math.Clamp(y, 0, height - 1);
                byte* p = basePtr + cy * rowStride + offset;
                sumB += p[0]; sumG += p[1]; sumR += p[2]; sumA += p[3];
            }
            for (int y = 0; y < height; y++)
            {
                buffer[y * 4] = (byte)(sumB / window);
                buffer[y * 4 + 1] = (byte)(sumG / window);
                buffer[y * 4 + 2] = (byte)(sumR / window);
                buffer[y * 4 + 3] = (byte)(sumA / window);
                int cyAdd = Math.Clamp(y + radius + 1, 0, height - 1);
                int cySub = Math.Clamp(y - radius, 0, height - 1);
                uint addB = basePtr[cyAdd * rowStride + offset], subB = basePtr[cySub * rowStride + offset];
                uint addG = basePtr[cyAdd * rowStride + offset + 1], subG = basePtr[cySub * rowStride + offset + 1];
                uint addR = basePtr[cyAdd * rowStride + offset + 2], subR = basePtr[cySub * rowStride + offset + 2];
                uint addA = basePtr[cyAdd * rowStride + offset + 3], subA = basePtr[cySub * rowStride + offset + 3];
                sumB += addB - subB; sumG += addG - subG; sumR += addR - subR; sumA += addA - subA;
            }
            for (int y = 0; y < height; y++)
            {
                byte* p = basePtr + y * rowStride + offset;
                p[0] = buffer[y * 4]; p[1] = buffer[y * 4 + 1]; p[2] = buffer[y * 4 + 2]; p[3] = buffer[y * 4 + 3];
            }
        }
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0) { path.AddRectangle(rect); return path; }
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
