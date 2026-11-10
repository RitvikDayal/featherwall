using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using FeatherWall.Tray;
using FeatherWall.Widgets;
using FeatherWall.Widgets.Themes;

namespace FeatherWall.Tests;

public class TightFormatTests
{
    /// <summary>GDI+ objects are not safe to share across threads, and the widgets paint from timer
    /// threads while the Style page paints on the UI thread.</summary>
    [Fact]
    public void Tight_IsOnePerThread()
    {
        var mine = ThemeFonts.Tight;
        StringFormat? other = null;
        var thread = new Thread(() => other = ThemeFonts.Tight);
        thread.Start();
        thread.Join();

        Assert.Same(mine, ThemeFonts.Tight);
        Assert.NotNull(other);
        Assert.NotSame(mine, other);
        Assert.True(other!.FormatFlags.HasFlag(StringFormatFlags.MeasureTrailingSpaces));
    }
}

public class PanelContentTests
{
    private static Bitmap Solid(Color c, int w = 40, int h = 20)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(c);
        return bmp;
    }

    [Fact]
    public void Hash_IsEqualForTheSamePixels()
    {
        using var a = Solid(Color.FromArgb(200, 10, 20, 30));
        using var b = Solid(Color.FromArgb(200, 10, 20, 30));
        Assert.Equal(PanelContent.Hash(a), PanelContent.Hash(b));
    }

    [Fact]
    public void Hash_ChangesWhenOnePixelChanges()
    {
        using var a = Solid(Color.Black);
        using var b = Solid(Color.Black);
        b.SetPixel(39, 19, Color.White);
        Assert.NotEqual(PanelContent.Hash(a), PanelContent.Hash(b));
    }

    [Fact]
    public void Hash_ChangesWithSize()
    {
        using var a = Solid(Color.Black, 40, 20);
        using var b = Solid(Color.Black, 20, 40);
        Assert.NotEqual(PanelContent.Hash(a), PanelContent.Hash(b));
    }
}

public class SettingsResourceTests
{
    [Fact]
    public void ClearAndDispose_DisposesTheOldChildren()
    {
        using var host = new Panel();
        var a = new Button();
        var b = new Label();
        host.Controls.Add(a);
        host.Controls.Add(b);

        ControlTree.ClearAndDispose(host);

        Assert.Empty(host.Controls);
        Assert.True(a.IsDisposed);
        Assert.True(b.IsDisposed);
    }

    [Fact]
    public void Backdrop_ReplaceDisposesThePreviousImage()
    {
        var first = new Bitmap(4, 4);
        var second = new Bitmap(4, 4);
        var holder = new BackdropHolder();

        holder.Replace(first);
        holder.Replace(second);

        Assert.Same(second, holder.Current);
        Assert.Throws<ArgumentException>(() => first.Width);
        holder.Dispose();
        Assert.Null(holder.Current);
        Assert.Throws<ArgumentException>(() => second.Width);
    }
}
