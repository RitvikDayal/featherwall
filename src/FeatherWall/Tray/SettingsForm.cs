using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using FeatherWall.Config;

namespace FeatherWall.Tray;

/// <summary>Settings panel (WinForms — loaded only when opened; the wallpaper pipeline never
/// touches it). Every control applies live, and a miniature of the real clock renderer sits at
/// the top so you can see the change without hunting for the desktop.
///
/// Laid out as strict label/control pairs in a 2-column grid. The previous version fed a
/// 4-column flow grid from helpers that added either one cell (a checkbox) or two (a labelled
/// input), so rows drifted out of step and labels ended up stranded from their controls —
/// "Font" sat above its combo box and "Volume %" was orphaned from its spinner.</summary>
public sealed class SettingsForm : Form
{
    private const int LabelColumnWidth = 200;
    private const int ContentWidth = 476;
    private const int RailWidth = 140;
    private const int TabHeight = 38;
    private const int FooterHeight = 58;

    private readonly Engine _engine;
    private readonly AppConfig _config;
    private readonly ClockPreview _preview;
    private bool _loading = true;

    private Button _colorButton = null!;
    private Label _colorHex = null!;

    public SettingsForm(Engine engine)
    {
        _engine = engine;
        _config = engine.Config;
        _preview = new ClockPreview(_config.Clock);

        Text = "FeatherWall";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        ShowInTaskbar = true;
        BackColor = Theme.Colors.Window;
        ForeColor = Theme.Colors.Text;
        ClientSize = new Size(RailWidth + ContentWidth + 60, 906);
        MinimumSize = new Size(RailWidth + ContentWidth + 76, 360);

        _preview.Width = RailWidth + ContentWidth + 20;
        _preview.Dock = DockStyle.Top;
        _preview.Height = 158;
        _preview.Margin = new Padding(0);

        var pages = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Colors.Window, AutoScroll = true };
        var rail = new Panel { Dock = DockStyle.Left, Width = RailWidth, BackColor = Theme.Colors.Window };

        AddPage(pages, rail, "Clock", BuildClockSection());
        AddPage(pages, rail, "Wallpaper", BuildWallpaperSection());
        AddPage(pages, rail, "Behaviour", BuildBehaviorSection());
        SelectPage(0);

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Colors.Window, Padding = new Padding(20, 16, 20, 8) };
        body.Controls.Add(pages);
        body.Controls.Add(rail);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = FooterHeight,
            Padding = new Padding(20, 12, 20, 12),
            BackColor = Theme.Colors.Window,
        };
        var close = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            Width = 104,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Colors.Accent,
            ForeColor = Color.White,
        };
        close.FlatAppearance.BorderSize = 0;
        footer.Controls.Add(close);
        AcceptButton = close;

        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(_preview);

        _loading = false;
    }

    private readonly List<(Button Tab, Control Page)> _pages = [];

    private void AddPage(Panel host, Panel rail, string title, Control page)
    {
        page.Dock = DockStyle.Top;
        page.Visible = false;
        host.Controls.Add(page);

        var tab = new Button
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = TabHeight,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Colors.Window,
            ForeColor = Theme.Colors.Text,
            Margin = new Padding(0),
        };
        tab.FlatAppearance.BorderSize = 0;
        int index = _pages.Count;
        tab.Click += (_, _) => SelectPage(index);

        // Dock.Top stacks in reverse add-order, so insert each new tab above the previous ones.
        rail.Controls.Add(tab);
        rail.Controls.SetChildIndex(tab, 0);

        _pages.Add((tab, page));
    }

    private void SelectPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            bool active = i == index;
            _pages[i].Page.Visible = active;
            _pages[i].Tab.BackColor = active ? Theme.Colors.Card : Theme.Colors.Window;
            _pages[i].Tab.ForeColor = active ? Theme.Colors.Accent : Theme.Colors.Text;
            _pages[i].Tab.Font = new Font("Segoe UI", 9.5f, active ? FontStyle.Bold : FontStyle.Regular);
        }
        ResizeToActivePage();
    }

    /// <summary>Pages differ a lot in length (Clock has 13 rows, Wallpaper 3). Sizing to the
    /// tallest would leave the short ones mostly empty, so the window follows the active page.</summary>
    private void ResizeToActivePage()
    {
        if (!IsHandleCreated) return;
        var page = _pages.FirstOrDefault(p => p.Page.Visible).Page;
        if (page is null) return;

        int content = Math.Max(page.PreferredSize.Height, _pages.Count * TabHeight) + 28;
        int desired = _preview.Height + content + FooterHeight;
        ClientSize = new Size(ClientSize.Width, desired);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ResizeToActivePage();
        CenterToScreen();
        // Spawned from the tray, our process may not hold foreground rights — force it.
        TopMost = true;
        Activate();
        BringToFront();
        TopMost = false;
    }

    // ---- sections ----------------------------------------------------------------------

    private Control BuildClockSection()
    {
        var (card, rows) = NewSection("Clock");

        rows.AddRow("Show clock", Check(_config.Clock.Enabled, v => { _config.Clock.Enabled = v; ApplyClock(); }));
        rows.AddRow("Time format", Choice(["12-hour", "24-hour"], _config.Clock.TwentyFourHour ? 1 : 0,
            i => { _config.Clock.TwentyFourHour = i == 1; ApplyClock(); }));
        rows.AddRow("Show seconds", Check(_config.Clock.ShowSeconds, v => { _config.Clock.ShowSeconds = v; ApplyClock(); }));
        rows.AddRow("Show date", Check(_config.Clock.ShowDate, v => { _config.Clock.ShowDate = v; ApplyClock(); }));
        rows.AddRow("Separator rule", Check(_config.Clock.Separator, v => { _config.Clock.Separator = v; ApplyClock(); }));
        rows.AddRow("Drop shadow", Check(_config.Clock.Shadow, v => { _config.Clock.Shadow = v; ApplyClock(); }));
        rows.AddRow("Position", BuildAnchorPicker());
        rows.AddRow("Margin X", Spinner(0, 2000, _config.Clock.MarginX, v => { _config.Clock.MarginX = v; ApplyClock(); }, "px"));
        rows.AddRow("Margin Y", Spinner(0, 2000, _config.Clock.MarginY, v => { _config.Clock.MarginY = v; ApplyClock(); }, "px"));
        rows.AddRow("Font size", Spinner(24, 400, (int)_config.Clock.FontSize, v => { _config.Clock.FontSize = v; ApplyClock(); }, "px"));
        rows.AddRow("Font", BuildFontPicker());
        rows.AddRow("Colour", BuildColorPicker());
        rows.AddRow("Opacity", Spinner(10, 100, (int)Math.Round(CurrentAlpha() / 2.55), v =>
        {
            SetColor(CurrentRgb(), (int)Math.Round(v * 2.55));
            UpdateColorLabel();
            ApplyClock();
        }, "%"));

        return card;
    }

    private Control BuildWallpaperSection()
    {
        var (card, rows) = NewSection("Wallpaper");

        rows.AddRow("Fit mode", Choice(["Fill (crop)", "Fit (letterbox)", "Stretch"], (int)_config.Fit, i =>
        {
            _config.Fit = (FitMode)i;
            _engine.RefreshWallpapers();
        }));
        rows.AddRow("Mute video", Check(_config.MuteVideo, v => { _config.MuteVideo = v; _engine.ApplyAudioSettings(); }));
        rows.AddRow("Volume", Spinner(0, 100, (int)Math.Round(_config.Volume * 100), v =>
        {
            _config.Volume = v / 100.0;
            _engine.ApplyAudioSettings();
        }, "%"));

        if (_engine.CurrentWallpaperCredit is { } credit)
            rows.AddRow("Credit", BuildCredit(credit));

        return card;
    }

    /// <summary>Shows who made the current gallery wallpaper, with a link to the source page.
    /// Only appears for gallery media; the user's own files have nothing to credit.</summary>
    private Control BuildCredit(Gallery.GalleryEntry entry)
    {
        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Colors.Card,
            Margin = new Padding(0, 2, 0, 2),
        };

        stack.Controls.Add(new Label
        {
            Text = entry.CreditLine,
            // AutoSize with a width cap so a long credit wraps and the row grows to fit it.
            // A fixed height silently clipped the licence off the second line.
            AutoSize = true,
            MaximumSize = new Size(ContentWidth - LabelColumnWidth - 16, 0),
            ForeColor = Theme.Colors.Text,
            Margin = new Padding(0, 2, 0, 2),
        });

        var source = new LinkLabel
        {
            Text = "View source",
            AutoSize = true,
            LinkColor = Theme.Colors.Accent,
            ActiveLinkColor = Theme.Colors.Accent,
            BackColor = Theme.Colors.Card,
            Margin = new Padding(0, 0, 0, 2),
        };
        source.LinkClicked += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(entry.SourcePage)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(entry.SourcePage)
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) { Common.Log.Warn($"Could not open source page: {ex.Message}"); }
        };
        stack.Controls.Add(source);

        return stack;
    }

    private Control BuildBehaviorSection()
    {
        var (card, rows) = NewSection("Behaviour");
        var pause = _config.Pause;

        rows.AddRow("Pause on fullscreen", Check(pause.OnFullscreen, v => { pause.OnFullscreen = v; _engine.SaveConfig(); }));
        rows.AddRow("Pause on battery saver", Check(pause.OnBatterySaver, v => { pause.OnBatterySaver = v; _engine.SaveConfig(); }));
        rows.AddRow("Pause in remote desktop", Check(pause.OnRemoteSession, v => { pause.OnRemoteSession = v; _engine.SaveConfig(); }));
        rows.AddRow("Run at startup", Check(Autostart.IsEnabled(), Autostart.SetEnabled));

        return card;
    }

    // ---- pickers -----------------------------------------------------------------------

    private Control BuildAnchorPicker()
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Colors.Card,
            Margin = new Padding(0, 2, 0, 2),
        };

        foreach (var anchor in Enum.GetValues<ClockAnchor>())
        {
            var cell = new RadioButton
            {
                Appearance = Appearance.Button,
                Size = new Size(38, 30),
                Margin = new Padding(2),
                Checked = _config.Clock.Anchor == anchor,
                Tag = anchor,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = AnchorGlyph(anchor),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Colors.Control,
                ForeColor = Theme.Colors.Text,
            };
            cell.FlatAppearance.BorderColor = Theme.Colors.Border;
            cell.FlatAppearance.CheckedBackColor = Theme.Colors.Accent;
            cell.CheckedChanged += (_, _) =>
            {
                if (_loading || !cell.Checked) return;
                _config.Clock.Anchor = (ClockAnchor)cell.Tag!;
                ApplyClock();
            };
            grid.Controls.Add(cell);
        }
        return grid;
    }

    /// <summary>Every family is drawn in its own face, so you pick a font by looking at it.</summary>
    private Control BuildFontPicker()
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            Width = 246,
            Height = 26,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 26,
            MaxDropDownItems = 14,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Colors.Control,
            ForeColor = Theme.Colors.Text,
            Margin = new Padding(0, 2, 0, 2),
        };

        using (var installed = new InstalledFontCollection())
            combo.Items.AddRange(installed.Families.Select(f => (object)f.Name).OrderBy(n => n).ToArray());

        combo.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            if (combo.Items[e.Index] is not string name) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;

            using var back = new SolidBrush(selected ? Theme.Colors.Accent : Theme.Colors.Control);
            e.Graphics.FillRectangle(back, e.Bounds);

            Font face;
            try { face = new Font(name, 11f); }
            catch { face = new Font(FontFamily.GenericSansSerif, 11f); }
            using (face)
            using (var text = new SolidBrush(selected ? Color.White : Theme.Colors.Text))
                e.Graphics.DrawString(name, face, text, e.Bounds.Left + 6, e.Bounds.Top + 4);
        };

        combo.Text = _config.Clock.FontFamily;
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || combo.SelectedItem is not string family) return;
            _config.Clock.FontFamily = family;
            ApplyClock();
        };
        combo.Leave += (_, _) =>
        {
            if (_loading || combo.Text.Length == 0) return;
            _config.Clock.FontFamily = combo.Text;
            ApplyClock();
        };
        return combo;
    }

    private Control BuildColorPicker()
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Colors.Card,
            Margin = new Padding(0, 2, 0, 2),
        };

        _colorButton = new Button
        {
            Width = 58,
            Height = 26,
            BackColor = CurrentRgb(),
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 10, 0),
        };
        _colorButton.FlatAppearance.BorderColor = Theme.Colors.Border;
        _colorButton.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = CurrentRgb(), FullOpen = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            SetColor(dialog.Color, CurrentAlpha());
            _colorButton.BackColor = dialog.Color;
            UpdateColorLabel();
            ApplyClock();
        };

        _colorHex = new Label
        {
            AutoSize = true,
            ForeColor = Theme.Colors.Subtle,
            Margin = new Padding(0, 5, 0, 0),
            Font = new Font("Consolas", 9f),
        };

        row.Controls.Add(_colorButton);
        row.Controls.Add(_colorHex);
        UpdateColorLabel();
        return row;
    }

    // ---- primitives --------------------------------------------------------------------

    private (Control Card, RowGrid Rows) NewSection(string title)
    {
        // Nested TableLayoutPanels only — Dock + AutoSize + FlowLayoutPanel together produce
        // zero-height cards here.
        var card = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Colors.Card,
            Padding = new Padding(16, 12, 16, 14),
            Margin = new Padding(0, 0, 0, 14),
        };

        card.Controls.Add(new Label
        {
            Text = title.ToUpperInvariant(),
            AutoSize = true,
            ForeColor = Theme.Colors.Subtle,
            Font = new Font("Segoe UI Semibold", 8.5f),
            Margin = new Padding(0, 0, 0, 10),
        });

        var rows = new RowGrid(this);
        card.Controls.Add(rows);
        return (card, rows);
    }

    /// <summary>A 2-column grid that only ever accepts complete label/control pairs — the
    /// structural guarantee the old free-form 4-column flow grid lacked.</summary>
    private sealed class RowGrid : TableLayoutPanel
    {
        public RowGrid(SettingsForm owner)
        {
            ColumnCount = 2;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Theme.Colors.Card;
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _ = owner;
        }

        public void AddRow(string label, Control control)
        {
            var caption = new Label
            {
                Text = label,
                AutoSize = true,
                ForeColor = Theme.Colors.Text,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 12, 6),
            };
            control.Anchor = AnchorStyles.Left;
            Controls.Add(caption);
            Controls.Add(control);
        }
    }

    private CheckBox Check(bool value, Action<bool> onChange)
    {
        var check = new CheckBox
        {
            Text = "",
            Checked = value,
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 5),
            ForeColor = Theme.Colors.Text,
            BackColor = Theme.Colors.Card,
            FlatStyle = FlatStyle.Flat,
        };
        check.FlatAppearance.BorderColor = Theme.Colors.Border;
        check.CheckedChanged += (_, _) => { if (!_loading) onChange(check.Checked); };
        return check;
    }

    private ComboBox Choice(string[] items, int selected, Action<int> onChange)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 190,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Colors.Control,
            ForeColor = Theme.Colors.Text,
            Margin = new Padding(0, 3, 0, 3),
        };
        combo.Items.AddRange(items.Cast<object>().ToArray());
        combo.SelectedIndex = Math.Clamp(selected, 0, items.Length - 1);
        combo.SelectedIndexChanged += (_, _) => { if (!_loading) onChange(combo.SelectedIndex); };
        return combo;
    }

    private Control Spinner(int min, int max, int value, Action<int> onChange, string suffix)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Colors.Card,
            Margin = new Padding(0, 2, 0, 2),
        };
        var numeric = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = 84,
            BackColor = Theme.Colors.Control,
            ForeColor = Theme.Colors.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 8, 0),
        };
        numeric.ValueChanged += (_, _) => { if (!_loading) onChange((int)numeric.Value); };

        row.Controls.Add(numeric);
        row.Controls.Add(new Label
        {
            Text = suffix,
            AutoSize = true,
            ForeColor = Theme.Colors.Subtle,
            Margin = new Padding(0, 5, 0, 0),
        });
        return row;
    }

    private static string AnchorGlyph(ClockAnchor anchor) => anchor switch
    {
        ClockAnchor.TopLeft => "↖", ClockAnchor.TopCenter => "↑", ClockAnchor.TopRight => "↗",
        ClockAnchor.CenterLeft => "←", ClockAnchor.Center => "•", ClockAnchor.CenterRight => "→",
        ClockAnchor.BottomLeft => "↙", ClockAnchor.BottomCenter => "↓", _ => "↘",
    };

    // ---- colour helpers ----------------------------------------------------------------

    private Color CurrentRgb()
    {
        var c = Widgets.ClockRenderer.ParseColor(_config.Clock.Color);
        return Color.FromArgb(255, c.R, c.G, c.B);
    }

    private int CurrentAlpha() => Widgets.ClockRenderer.ParseColor(_config.Clock.Color).A;

    private void SetColor(Color rgb, int alpha) =>
        _config.Clock.Color = $"#{alpha:X2}{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";

    private void UpdateColorLabel() => _colorHex.Text = _config.Clock.Color.ToUpperInvariant();

    private void ApplyClock()
    {
        if (_loading) return;
        _engine.RefreshClock();
        _preview.Invalidate();
    }
}
