using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace FeatherWall.Widgets;

/// <summary>A fingerprint of a painted panel, so a clock tick that changes only the time does not
/// re-shadow and re-present the battery and record panels beside it.</summary>
public static class PanelContent
{
    public static byte[] Hash(Bitmap bmp)
    {
        var rect = new Rectangle(Point.Empty, bmp.Size);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            int rowBytes = bmp.Width * 4;
            var pixels = new byte[rowBytes * bmp.Height + 8];
            BitConverter.TryWriteBytes(pixels.AsSpan(0, 4), bmp.Width);
            BitConverter.TryWriteBytes(pixels.AsSpan(4, 4), bmp.Height);
            for (int y = 0; y < bmp.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, 8 + y * rowBytes, rowBytes);
            return SHA256.HashData(pixels);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
