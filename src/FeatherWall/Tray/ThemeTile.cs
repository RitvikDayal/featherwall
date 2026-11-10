using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FeatherWall.Config;

namespace FeatherWall.Tray;

/// <summary>One theme on the Style page: its live preview, its name, and an accent outline when it
/// is the one in use. The preview is rendered lazily at the tile's real pixel size, so it stays
/// sharp at any DPI.</summary>
internal sealed class ThemeTile : Control
{
    private const int CaptionHeight = 26;
    private readonly Func<WidgetTheme, Size, Bitmap> _render;
    private Bitmap? _image;
    private bool _selected;

    public WidgetTheme Style { get; }

    public ThemeTile(WidgetTheme style, Func<WidgetTheme, Size, Bitmap> render)
    {
        Style = style;
        _render = render;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        BackColor = Theme.Colors.Card;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Invalidate(); }
    }

    /// <summary>The accent or the backdrop changed: draw the preview again on the next paint.</summary>
    public void Rerender()
    {
        _image?.Dispose();
        _image = null;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var area = new Rectangle(2, 2, Math.Max(Width - 4, 1), Math.Max(Height - CaptionHeight - 4, 1));
        if (_image is null || _image.Size != area.Size)
        {
            _image?.Dispose();
            try { _image = _render(Style, area.Size); }
            catch (Exception ex) { Common.Log.Error($"Style preview for {Style} failed", ex); }
        }
        if (_image is not null) g.DrawImageUnscaled(_image, area.Location);

        using (var border = new Pen(_selected ? Theme.Colors.Accent : Theme.Colors.Border, _selected ? 3f : 1f))
            g.DrawRectangle(border, 1, 1, Width - 3, Height - CaptionHeight - 2);

        var caption = new Rectangle(0, Height - CaptionHeight, Width, CaptionHeight);
        using var font = new Font("Segoe UI", 9.5f, _selected ? FontStyle.Bold : FontStyle.Regular);
        TextRenderer.DrawText(g, Style.ToString() + (_selected ? "  ✓" : ""), font, caption, _selected ? Theme.Colors.Accent : Theme.Colors.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image?.Dispose();
        base.Dispose(disposing);
    }
}
