using System.Drawing;
using System.Drawing.Imaging;
using FeatherWall.Config;
using FeatherWall.Widgets;

namespace FeatherWall.Tests;

/// <summary>The record's face and rim drawn separately, so they can live on separate layers — the
/// face turning on the compositor, the rim held still above it. Composed in order they must be
/// exactly the old single paint, or Classic changes look on upgrade.</summary>
public class DiscPaintSplitTests
{
    private static readonly Rectangle Box = new(0, 0, 120, 120);

    private static Bitmap Face()
    {
        var face = new Bitmap(120, 120, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(face);
        using var brush = new SolidBrush(Color.FromArgb(255, 200, 80, 40));
        g.FillEllipse(brush, 0, 0, 120, 120);
        g.FillRectangle(Brushes.White, 55, 5, 10, 30); // asymmetric, so a rotation shows
        return face;
    }

    private static byte[] Bytes(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var bytes = new byte[data.Stride * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { bmp.UnlockBits(data); }
    }

    private static Bitmap Canvas(int w = 120, int h = 120) => new(w, h, PixelFormat.Format32bppPArgb);

    [Fact]
    public void Paint_IsFaceThenRim()
    {
        var config = new DiscConfig { ShowProgress = true };
        using var face = Face();
        using var whole = Canvas();
        using var split = Canvas();

        using (var g = Graphics.FromImage(whole))
            DiscRenderer.Paint(g, Box, config, face, turns: 0.3f, progress: 0.4f, playing: true);
        using (var g = Graphics.FromImage(split))
        {
            DiscRenderer.PaintFace(g, Box, face, turns: 0.3f, playing: true);
            DiscRenderer.PaintRim(g, Box, config, progress: 0.4f, playing: true);
        }

        Assert.Equal(Bytes(whole), Bytes(split));
    }

    [Fact]
    public void PaintRim_LeavesTheCentreClear()
    {
        using var bmp = Canvas();
        using (var g = Graphics.FromImage(bmp))
            DiscRenderer.PaintRim(g, Box, new DiscConfig { ShowProgress = true }, progress: 0.4f, playing: true);

        Assert.Equal(0, bmp.GetPixel(60, 60).A);
    }

    [Fact]
    public void PaintRim_ShowProgressOff_DrawsNothing()
    {
        using var bmp = Canvas();
        using (var g = Graphics.FromImage(bmp))
            DiscRenderer.PaintRim(g, Box, new DiscConfig { ShowProgress = false }, progress: 0.4f, playing: true);

        Assert.All(Bytes(bmp), b => Assert.Equal(0, b));
    }

    [Fact]
    public void NowPlaying_WithoutFace_LeavesDiscCentreClear()
    {
        var config = new DiscConfig { Size = 100, ShowProgress = true };
        var reading = NowPlayingSource.Read("Blue in Green", "Miles Davis", isPlaying: true);
        var m = NowPlayingRenderer.Measure(config, reading);
        using var face = Face();
        using var bmp = Canvas(m.Total.Width, m.Total.Height);

        using (var g = Graphics.FromImage(bmp))
            NowPlayingRenderer.Paint(g, m, config, reading, face, turns: 0f, progress: 0.4f, includeFace: false);

        var centre = new Point(m.DiscBox.X + m.DiscBox.Width / 2, m.DiscBox.Y + m.DiscBox.Height / 2);
        Assert.Equal(0, bmp.GetPixel(centre.X, centre.Y).A);
        bool titleInked = false;
        for (int x = m.TitleBox.Left; x < m.TitleBox.Right && !titleInked; x++)
            for (int y = m.TitleBox.Top; y < m.TitleBox.Bottom && !titleInked; y++)
                titleInked = bmp.GetPixel(x, y).A > 0;
        Assert.True(titleInked, "the title must still be drawn when the face is left out");
    }
}
