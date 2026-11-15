using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PrismOS.Tray;

namespace PrismOS.IconGen;

/// <summary>Regenerates src/PrismOS/prismos.ico from the PrismOS mark.
///
/// The .ico is a build-time artefact, not something the app reads: the application icon comes
/// from the exe's icon resource, and the tray icon is drawn at run time by the same code. This
/// tool exists so that the asset and the drawing cannot disagree — change PrismIcon and re-run
/// it, rather than hand-editing a binary.
///
/// The file holds one entry per size Windows 11 asks for: 16, 20, 24, 32, 40, 48, 64, 128 and
/// 256. The smaller sizes are stored as DIBs and the two largest as PNG, which is what the shell
/// expects: a 16-pixel entry that is a PNG is read by some tools and not others.</summary>
internal static class Program
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    private static int Main(string[] args)
    {
        string? target = args.Length > 0 ? args[0] : DefaultIconPath();
        if (target is null)
        {
            Console.Error.WriteLine("Could not find the repository root. Pass the .ico path as an argument.");
            return 1;
        }

        string full = Path.GetFullPath(target);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var entries = new List<(int Size, byte[] Data)>();
        foreach (int size in Sizes)
        {
            using var bitmap = PrismIcon.ToBitmap(size);
            entries.Add((size, size >= 256 ? Png(bitmap) : Dib(bitmap)));
        }

        WriteIco(full, entries);
        Console.WriteLine($"Wrote {full} ({Sizes.Length} sizes: {string.Join(", ", Sizes)})");
        return 0;
    }

    /// <summary>src/PrismOS/prismos.ico, found by walking up from wherever the tool was run.
    /// The working directory of a `dotnet run` is the build output folder, not the project
    /// folder, so a relative path would land somewhere else entirely.</summary>
    private static string? DefaultIconPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "src", "PrismOS", "PrismOS.csproj");
            if (File.Exists(candidate))
                return Path.Combine(dir.FullName, "src", "PrismOS", "prismos.ico");
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>The ICO container: a six-byte header, one sixteen-byte directory entry per
    /// image, then the images.</summary>
    private static void WriteIco(string path, List<(int Size, byte[] Data)> entries)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);                       // reserved
        writer.Write((ushort)1);                       // 1 = icon
        writer.Write((ushort)entries.Count);

        int offset = 6 + 16 * entries.Count;
        foreach (var (size, data) in entries)
        {
            // 0 means 256 in a directory entry.
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);                      // palette size
            writer.Write((byte)0);                      // reserved
            writer.Write((ushort)1);                    // colour planes
            writer.Write((ushort)32);                   // bits per pixel
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }

        foreach (var (_, data) in entries) writer.Write(data);
    }

    private static byte[] Png(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>A DIB entry: a BITMAPINFOHEADER whose height is twice the image height (the XOR
    /// image and the AND mask stacked), the pixels bottom-up in BGRA, then the mask. The alpha
    /// channel of the 32-bit pixels is what Windows uses for transparency; the mask is all
    /// zeroes because the shape is already cut out in the alpha.</summary>
    private static byte[] Dib(Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;

        byte[] pixels;
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, (height - 1 - y) * width * 4, width * 4);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        // A 1-bit mask, rows padded to four bytes. All zero: opaque where the alpha says so.
        int maskStride = ((width + 31) / 32) * 4;
        byte[] mask = new byte[maskStride * height];

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)40);                                  // biSize
        writer.Write(width);                                     // biWidth
        writer.Write(height * 2);                                // biHeight: image + mask
        writer.Write((ushort)1);                                 // biPlanes
        writer.Write((ushort)32);                                // biBitCount
        writer.Write((uint)0);                                   // biCompression: BI_RGB
        writer.Write((uint)(pixels.Length + mask.Length));       // biSizeImage
        writer.Write((uint)0);                                   // biXPelsPerMeter
        writer.Write((uint)0);                                   // biYPelsPerMeter
        writer.Write((uint)0);                                   // biClrUsed
        writer.Write((uint)0);                                   // biClrImportant
        writer.Write(pixels);
        writer.Write(mask);
        writer.Flush();
        return stream.ToArray();
    }
}
