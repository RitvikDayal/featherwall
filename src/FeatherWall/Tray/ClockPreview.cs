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
    private readonly Timer _timer;

    public ClockPreview(ClockConfig config)
    {
        _config = config;
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
