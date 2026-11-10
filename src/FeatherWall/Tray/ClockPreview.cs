using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FeatherWall.Config;
using FeatherWall.Widgets;
using Timer = System.Windows.Forms.Timer;

namespace FeatherWall.Tray;

/// <summary>Live miniature of the desktop clock, drawn through <see cref="ClockRenderer"/> — the
/// same code that paints the wallpaper — so font, weight, rule and date all preview truthfully.
/// The nine anchor positions are honoured inside the preview box, so picking "bottom right"
/// actually shows the clock in the bottom right.</summary>
public sealed class ClockPreview : Control
{
    private readonly ClockConfig _config;
    private readonly Func<FeatherWall.Widgets.Themes.WidgetStyle> _style;
    private readonly Timer _timer;

    public ClockPreview(ClockConfig config, Func<FeatherWall.Widgets.Themes.WidgetStyle>? style = null)
    {
        _config = config;
        _style = style ?? (() => FeatherWall.Widgets.Themes.WidgetStyle.Classic);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _timer = new Timer { Interval = 1000 };
        _timer.Tick += (_, _) => Invalidate();
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Stand-in wallpaper: a soft diagonal so light text and shadows read honestly.
        using (var backdrop = new LinearGradientBrush(ClientRectangle,
                   Theme.Colors.Preview,
                   ControlPaint.Light(Theme.Colors.Preview, 0.35f),
                   45f))
            g.FillRectangle(backdrop, ClientRectangle);

        using (var frame = new Pen(Theme.Colors.Border))
            g.DrawRectangle(frame, 0, 0, Width - 1, Height - 1);

        if (!_config.Enabled)
        {
            DrawCentredHint(g, "Clock is off");
            return;
        }

        var style = _style();
        if (style.Theme == WidgetTheme.Editorial)
        {
            PaintEditorial(g, style);
            return;
        }
        if (WidgetsConfig.IsGroup(style.Theme))
        {
            PaintGroupClock(g, style);
            return;
        }

        // Scale the real widget down until it fits with a comfortable inset.
        var full = ClockRenderer.Measure(_config, DateTime.Now);
        float inset = 16f;
        float fit = Math.Min(
            (Width - inset * 2) / Math.Max(full.Total.Width, 1),
            (Height - inset * 2) / Math.Max(full.Total.Height, 1));
        float scale = Math.Min(fit, 1f);

        var metrics = ClockRenderer.Measure(_config, DateTime.Now, scale);
        if (metrics.Total.Width <= 0 || metrics.Total.Height <= 0) return;

        var pos = ClockLayout.Position(
            new Interop.RECT(0, 0, Width, Height),
            metrics.Total.Width, metrics.Total.Height,
            _config.Anchor,
            (int)Math.Round(_config.MarginX * scale),
            (int)Math.Round(_config.MarginY * scale));

        // Keep the miniature inside the box even at extreme margins.
        int x = Math.Clamp(pos.X, 0, Math.Max(Width - metrics.Total.Width, 0));
        int y = Math.Clamp(pos.Y, 0, Math.Max(Height - metrics.Total.Height, 0));

        var state = g.Save();
        g.TranslateTransform(x, y);
        ClockRenderer.Paint(g, _config, metrics, ClockRenderer.ParseColor(_config.Color), metrics.Total);
        g.Restore(state);
    }

    /// <summary>The Editorial clock, through its own renderer and the same baked shadow the desktop
    /// gets, scaled to fit exactly as the Classic path is.</summary>
    private void PaintEditorial(Graphics g, FeatherWall.Widgets.Themes.WidgetStyle style)
    {
        var now = DateTime.Now;
        var full = FeatherWall.Widgets.Themes.Editorial.MeasureClock(_config, now, style.Size);
        float inset = 8f;
        float fit = Math.Min(
            (Width - inset * 2) / Math.Max(full.Total.Width, 1),
            (Height - inset * 2) / Math.Max(full.Total.Height, 1));
        float shrink = Math.Min(fit, 1f);
        float scale = style.Size * shrink;

        var m = FeatherWall.Widgets.Themes.Editorial.MeasureClock(_config, now, scale);
        if (m.Total.Width <= 0 || m.Total.Height <= 0) return;

        using var surface = new Bitmap(m.Total.Width, m.Total.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var sg = Graphics.FromImage(surface))
            FeatherWall.Widgets.Themes.Editorial.PaintClock(sg, m, style.Accent);
        FeatherWall.Widgets.Themes.BakedShadow.DrawBehind(surface, FeatherWall.Widgets.Themes.ShadowRecipe.Default, scale);

        var pos = ClockLayout.Position(
            new Interop.RECT(0, 0, Width, Height),
            m.Total.Width, m.Total.Height,
            _config.Anchor,
            (int)Math.Round(_config.MarginX * shrink),
            (int)Math.Round(_config.MarginY * shrink));
        int x = Math.Clamp(pos.X, 0, Math.Max(Width - m.Total.Width, 0));
        int y = Math.Clamp(pos.Y, 0, Math.Max(Height - m.Total.Height, 0));
        g.DrawImageUnscaled(surface, x, y);
    }

    /// <summary>A group theme's clock alone — the dial, the stacked hours, the dock — through the
    /// theme's own layout, shrunk until it fits the preview box and centred in it.</summary>
    private void PaintGroupClock(Graphics g, FeatherWall.Widgets.Themes.WidgetStyle style)
    {
        var theme = FeatherWall.Widgets.Themes.GroupThemes.For(style.Theme);
        var box = new Rectangle(0, 0, Width, Height);
        IReadOnlyList<FeatherWall.Widgets.Themes.GroupPanel> panels = [];
        float f = 1f, scale = style.Size;
        for (int i = 0; i < 14; i++, f *= 0.85f)
        {
            scale = style.Size * f;
            var input = new FeatherWall.Widgets.Themes.GroupInput(DateTime.Now, _config, new DiscConfig(), new HaloConfig(),
                null, null, style.Accent, scale, f, ClockAnchor.Center, box, 48);
            panels = theme.Layout(input);
            if (panels.All(p => box.Contains(p.Bounds))) break;
        }
        foreach (var panel in panels)
        {
            using var surface = new Bitmap(panel.Bounds.Width, panel.Bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var sg = Graphics.FromImage(surface)) panel.Paint(sg);
            FeatherWall.Widgets.Themes.BakedShadow.DrawBehind(surface, FeatherWall.Widgets.Themes.ShadowRecipe.Default, scale);
            // Centred, whatever corner the theme would choose: this box shows the look, not the place.
            int x = (Width - surface.Width) / 2, y = (Height - surface.Height) / 2;
            g.DrawImageUnscaled(surface, x, y);
        }
    }

    private void DrawCentredHint(Graphics g, string text)
    {
        using var font = new Font("Segoe UI", 9f);
        using var brush = new SolidBrush(Theme.Colors.Subtle);
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, brush, (Width - size.Width) / 2f, (Height - size.Height) / 2f);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
