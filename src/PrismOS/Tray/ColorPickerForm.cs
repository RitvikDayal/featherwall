using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PrismOS.Tray;

/// <summary>A colour picker: an SV square, a hue slider, an opacity slider, a hex field and the
/// four channels as numbers.
///
/// The Windows colour dialog is a modal dialog with a colour table and a "custom colours" tab
/// that opens a second dialog, and it cannot express alpha at all — so every colour in this app
/// that carries an alpha was edited by picking a colour in one dialog and then typing its alpha
/// into a spinner next to it, with the two never agreeing. This is one dialog that shows all of
/// it at once and keeps the parts in step.
///
/// The colours it offers are the ones the app uses: #AARRGGBB for the widgets, and #RRGGBB for
/// the accent, which the accent parser matches exactly and would silently reject otherwise.</summary>
public sealed class ColorPickerForm : Form
{
    private readonly bool _allowAlpha;
    private readonly Color _original;

    private readonly SaturationValueBox _sv;
    private readonly HueStrip _hue;
    private readonly AlphaStrip _alpha;
    private readonly ColourSwatch _preview;
    private readonly ColourSwatch _originalPreview;
    private readonly TextBox _hex;
    private readonly NumericUpDown _r;
    private readonly NumericUpDown _g;
    private readonly NumericUpDown _b;
    private readonly NumericUpDown _a;

    /// <summary>True while the controls are being set from each other, so that a change to one
    /// does not bounce back through the others and rewrite what was just typed.</summary>
    private bool _loading = true;

    /// <summary>The chosen colour, with the alpha it was given. Only meaningful when the dialog
    /// was closed with OK.</summary>
    public Color SelectedColor { get; private set; }

    public ColorPickerForm(Color current, bool allowAlpha = true)
    {
        _allowAlpha = allowAlpha;
        _original = allowAlpha ? current : Color.FromArgb(255, current.R, current.G, current.B);
        SelectedColor = _original;

        Text = "Choose a colour";
        Icon = PrismIcon.CreateIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Theme.Colors.Window;
        ForeColor = Theme.Colors.Text;
        ClientSize = new Size(620, 470);

        _sv = new SaturationValueBox { Location = new Point(20, 20), Size = new Size(300, 300) };
        _hue = new HueStrip { Location = new Point(332, 20), Size = new Size(28, 300) };
        _alpha = new AlphaStrip { Location = new Point(368, 20), Size = new Size(28, 300) };

        _preview = new ColourSwatch { Location = new Point(20, 340), Size = new Size(60, 60) };
        _originalPreview = new ColourSwatch { Location = new Point(92, 340), Size = new Size(60, 60) };
        _originalPreview.Colour = _original;

        _hex = new TextBox
        {
            Location = new Point(172, 362),
            Size = new Size(132, 26),
            Font = new Font("Consolas", 10f),
            BackColor = Theme.Colors.Control,
            ForeColor = Theme.Colors.Text,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _hex.TextChanged += (_, _) => OnHexChanged();

        _r = Channel(330, 'R');
        _g = Channel(390, 'G');
        _b = Channel(450, 'B');
        _a = Channel(510, 'A');
        _a.Value = _original.A;
        _a.ValueChanged += (_, _) => OnChannelChanged();

        var hexLabel = Caption("Hex", 172, 340);
        var newLabel = Caption("New", 20, 402);
        var oldLabel = Caption("Original", 92, 402);

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(502, 414),
            Size = new Size(96, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Colors.Accent,
            ForeColor = Color.White,
        };
        ok.FlatAppearance.BorderSize = 0;
        ok.Click += (_, _) => SelectedColor = CurrentFromControls();

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(398, 414),
            Size = new Size(96, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Colors.Control,
            ForeColor = Theme.Colors.Text,
        };
        cancel.FlatAppearance.BorderColor = Theme.Colors.Border;

        Controls.AddRange([
            _sv, _hue, _alpha, _preview, _originalPreview,
            _hex, _r, _g, _b, _a,
            hexLabel, newLabel, oldLabel,
            ok, cancel,
        ]);

        AcceptButton = ok;
        CancelButton = cancel;

        _sv.Changed += OnVisualChanged;
        _hue.Changed += OnVisualChanged;
        _alpha.Changed += OnVisualChanged;

        if (!_allowAlpha)
        {
            _alpha.Visible = false;
            _a.Visible = false;
            var alphaLabel = Caption("A", 510, 340);
            alphaLabel.Visible = false;
        }

        LoadColour(_original);
        _loading = false;
    }

    private Label Caption(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(130, 18),
        ForeColor = Theme.Colors.Subtle,
        AutoSize = false,
    };

    private NumericUpDown Channel(int x, char name)
    {
        var caption = Caption(name.ToString(), x, 340);
        Controls.Add(caption);
        var box = new NumericUpDown
        {
            Location = new Point(x, 362),
            Size = new Size(56, 26),
            Minimum = 0,
            Maximum = 255,
            BackColor = Theme.Colors.Control,
            ForeColor = Theme.Colors.Text,
            BorderStyle = BorderStyle.FixedSingle,
        };
        box.ValueChanged += (_, _) => OnChannelChanged();
        return box;
    }

    private void LoadColour(Color colour)
    {
        _loading = true;
        try
        {
            var hsv = Hsv.FromRgb(colour);
            _hue.Hue = hsv.Hue;
            _sv.Saturation = hsv.Saturation;
            _sv.Value = hsv.Value;
            _alpha.Alpha = AlphaScale.ToFraction(colour.A);
            _r.Value = colour.R;
            _g.Value = colour.G;
            _b.Value = colour.B;
            _a.Value = colour.A;
            _hex.Text = _allowAlpha ? ColorHex.ToHex(colour) : ColorHex.ToRgbHex(colour);
        }
        finally
        {
            _loading = false;
        }
        RefreshViews();
    }

    /// <summary>The colour the SV square and the sliders are describing.</summary>
    private Color VisualColour()
    {
        var rgb = new Hsv(_hue.Hue, _sv.Saturation, _sv.Value).ToRgb();
        int alpha = _allowAlpha ? AlphaScale.ToAlpha(_alpha.Alpha) : 255;
        return Color.FromArgb(alpha, rgb.R, rgb.G, rgb.B);
    }

    private Color CurrentFromControls() => Color.FromArgb(
        _allowAlpha ? (int)_a.Value : 255,
        (int)_r.Value, (int)_g.Value, (int)_b.Value);

    private void OnVisualChanged()
    {
        if (_loading) return;
        var colour = VisualColour();
        _loading = true;
        try
        {
            _r.Value = colour.R;
            _g.Value = colour.G;
            _b.Value = colour.B;
            if (_allowAlpha) _a.Value = colour.A;
            // The hex field has to follow too. Left stale, it kept showing the colour the
            // picker opened with, and a person who picked a colour and then edited the hex
            // would be editing the old one.
            _hex.Text = _allowAlpha ? ColorHex.ToHex(colour) : ColorHex.ToRgbHex(colour);
        }
        finally
        {
            _loading = false;
        }
        RefreshViews();
    }

    private void OnChannelChanged()
    {
        if (_loading) return;
        var colour = CurrentFromControls();
        _loading = true;
        try
        {
            var hsv = Hsv.FromRgb(colour);
            _hue.Hue = hsv.Hue;
            _sv.Saturation = hsv.Saturation;
            _sv.Value = hsv.Value;
            _alpha.Alpha = AlphaScale.ToFraction(colour.A);
            _hex.Text = _allowAlpha ? ColorHex.ToHex(colour) : ColorHex.ToRgbHex(colour);
        }
        finally
        {
            _loading = false;
        }
        RefreshViews();
    }

    private void OnHexChanged()
    {
        if (_loading) return;
        if (!ColorHex.TryParse(_hex.Text, out var colour)) return;
        _loading = true;
        try
        {
            var hsv = Hsv.FromRgb(colour);
            _hue.Hue = hsv.Hue;
            _sv.Saturation = hsv.Saturation;
            _sv.Value = hsv.Value;
            _alpha.Alpha = AlphaScale.ToFraction(colour.A);
            _r.Value = colour.R;
            _g.Value = colour.G;
            _b.Value = colour.B;
            _a.Value = colour.A;
        }
        finally
        {
            _loading = false;
        }
        RefreshViews();
    }

    private void RefreshViews()
    {
        var colour = CurrentFromControls();
        _preview.Colour = colour;
        _sv.Hue = _hue.Hue;
        _alpha.Colour = colour;
        _sv.Invalidate();
        _hue.Invalidate();
        _alpha.Invalidate();
        _preview.Invalidate();
    }
}

/// <summary>A square showing how a colour changes with saturation and value, for one hue.</summary>
internal sealed class SaturationValueBox : Control
{
    private double _hue = 210;
    private double _saturation = 0.6;
    private double _value = 0.9;

    public event Action? Changed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Hue
    {
        get => _hue;
        set { _hue = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Saturation
    {
        get => _saturation;
        set { _saturation = Math.Clamp(value, 0, 1); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 1); Invalidate(); }
    }

    public SaturationValueBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                  ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Colors.Control;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var rect = new Rectangle(0, 0, Width, Height);
        var pure = new Hsv(_hue, 1, 1).ToRgb();

        // Left is white, right is the pure hue: that is the saturation axis.
        using (var saturation = new LinearGradientBrush(
                   new Rectangle(0, 0, Math.Max(1, Width), 1), Color.White, pure, LinearGradientMode.Horizontal))
        {
            g.FillRectangle(saturation, rect);
        }
        // Top is full brightness, bottom is black: that is the value axis.
        using (var value = new LinearGradientBrush(
                   new Rectangle(0, 0, 1, Math.Max(1, Height)), Color.Transparent, Color.Black, LinearGradientMode.Vertical))
        {
            g.FillRectangle(value, rect);
        }

        float x = (float)(_saturation * (Width - 1));
        float y = (float)((1 - _value) * (Height - 1));
        DrawMarker(g, new PointF(x, y), pure);
    }

    private static void DrawMarker(Graphics g, PointF at, Color ring)
    {
        const float radius = 6f;
        using var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
        g.FillEllipse(shadow, at.X - radius, at.Y - radius, radius * 2, radius * 2);
        using var white = new Pen(Color.White, 1.8f);
        g.DrawEllipse(white, at.X - radius, at.Y - radius, radius * 2, radius * 2);
        using var colour = new Pen(ring, 1.2f);
        g.DrawEllipse(colour, at.X - radius + 1, at.Y - radius + 1, radius * 2 - 2, radius * 2 - 2);
    }

    protected override void OnMouseDown(MouseEventArgs e) { Pick(e); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) Pick(e);
        base.OnMouseMove(e);
    }

    private void Pick(MouseEventArgs e)
    {
        if (Width <= 1 || Height <= 1) return;
        _saturation = Math.Clamp(e.X / (double)(Width - 1), 0, 1);
        _value = Math.Clamp(1 - e.Y / (double)(Height - 1), 0, 1);
        Invalidate();
        Changed?.Invoke();
    }
}

/// <summary>A vertical strip of every hue, for the slider beside the SV square.</summary>
internal sealed class HueStrip : Control
{
    private double _hue = 210;

    public event Action? Changed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Hue
    {
        get => _hue;
        set { _hue = (value % 360 + 360) % 360; Invalidate(); }
    }

    public HueStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                  ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Colors.Control;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var rect = new Rectangle(0, 0, Width, Height);

        // The ramp is drawn a row at a time rather than with a multi-stop gradient brush: one
        // hue per pixel row is exact, and it cannot show the seams a banded gradient would.
        for (int row = 0; row < Height; row++)
        {
            double hue = 360.0 * (Height <= 1 ? 0 : row / (double)(Height - 1));
            using var brush = new SolidBrush(new Hsv(hue, 1, 1).ToRgb());
            g.FillRectangle(brush, 0, row, Width, 1);
        }

        float y = (float)(_hue / 360.0 * (Height - 1));
        using var pen = new Pen(Color.White, 2f);
        g.DrawLine(pen, 0, y, Width, y);
        using var shadow = new Pen(Color.FromArgb(120, 0, 0, 0), 1f);
        g.DrawLine(shadow, 0, y + 1, Width, y + 1);
    }

    protected override void OnMouseDown(MouseEventArgs e) { Pick(e); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) Pick(e);
        base.OnMouseMove(e);
    }

    private void Pick(MouseEventArgs e)
    {
        if (Height <= 1) return;
        Hue = 360.0 * Math.Clamp(e.Y / (double)(Height - 1), 0, 1);
        Changed?.Invoke();
    }
}

/// <summary>A vertical strip showing the colour at every opacity, over a chequerboard so a
/// half-transparent colour reads as half-transparent.</summary>
internal sealed class AlphaStrip : Control
{
    private double _alpha = 1;
    private Color _colour = Color.White;

    public event Action? Changed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Alpha
    {
        get => _alpha;
        set { _alpha = Math.Clamp(value, 0, 1); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Colour
    {
        get => _colour;
        set { _colour = value; Invalidate(); }
    }

    public AlphaStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                  ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Colors.Control;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var rect = new Rectangle(0, 0, Width, Height);

        // Chequerboard, so the colour reads against something.
        const int cell = 6;
        using (var light = new SolidBrush(Color.FromArgb(180, 180, 180)))
        using (var dark = new SolidBrush(Color.FromArgb(120, 120, 120)))
        {
            for (int row = 0; row < Height; row += cell)
                for (int column = 0; column < Width; column += cell)
                    g.FillRectangle(((column / cell + row / cell) % 2 == 0) ? light : dark,
                        new Rectangle(column, row, cell, cell));
        }

        using (var brush = new LinearGradientBrush(
                   new Rectangle(0, 0, 1, Math.Max(1, Height)),
                   Color.FromArgb(0, _colour), Color.FromArgb(255, _colour), LinearGradientMode.Vertical))
        {
            g.FillRectangle(brush, rect);
        }

        float y = (float)((1 - _alpha) * (Height - 1));
        using var pen = new Pen(Color.White, 2f);
        g.DrawLine(pen, 0, y, Width, y);
        using var shadow = new Pen(Color.FromArgb(120, 0, 0, 0), 1f);
        g.DrawLine(shadow, 0, y + 1, Width, y + 1);
    }

    protected override void OnMouseDown(MouseEventArgs e) { Pick(e); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) Pick(e);
        base.OnMouseMove(e);
    }

    private void Pick(MouseEventArgs e)
    {
        if (Height <= 1) return;
        Alpha = Math.Clamp(1 - e.Y / (double)(Height - 1), 0, 1);
        Changed?.Invoke();
    }
}

/// <summary>A square of one colour, over a chequerboard when it is not fully opaque.</summary>
internal sealed class ColourSwatch : Control
{
    private Color _colour = Color.White;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Colour
    {
        get => _colour;
        set { _colour = value; Invalidate(); }
    }

    public ColourSwatch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                  ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var rect = new Rectangle(0, 0, Width, Height);
        if (_colour.A < 255)
        {
            const int cell = 8;
            using (var light = new SolidBrush(Color.FromArgb(200, 200, 200)))
            using (var dark = new SolidBrush(Color.FromArgb(150, 150, 150)))
            {
                for (int row = 0; row < Height; row += cell)
                    for (int column = 0; column < Width; column += cell)
                        g.FillRectangle(((column / cell + row / cell) % 2 == 0) ? light : dark,
                            new Rectangle(column, row, cell, cell));
            }
        }
        using var brush = new SolidBrush(_colour);
        g.FillRectangle(brush, rect);
        using var border = new Pen(Theme.Colors.Border, 1f);
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
}
