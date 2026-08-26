using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using FeatherWall.Config;
using FeatherWall.Widgets;

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
        AddPage(pages, rail, "Date", BuildDateSection());
        AddPage(pages, rail, "Info", BuildInfoSection());
        AddPage(pages, rail, "Battery", BuildHaloSection());
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
        // "Show date" and "Separator rule" live on the Date page — two controls bound to one
        // property desync the moment either is used.
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

    /// <summary>The date line used to be unstyleable — "Segoe UI" at 16% of the time's size,
    /// hardcoded. Asked for on r/coolgithubprojects. Every control here defaults to the value
    /// that reproduces the old rendering, so an untouched install looks the same.</summary>
    private Control BuildDateSection()
    {
        var (card, rows) = NewSection("Date");
        var clock = _config.Clock;

        rows.AddRow("Show date", Check(clock.ShowDate, v => { clock.ShowDate = v; ApplyClock(); }));
        rows.AddRow("Separator rule", Check(clock.Separator, v => { clock.Separator = v; ApplyClock(); }));
        rows.AddRow("Font", BuildFontPicker(
            () => clock.DateFontFamily,
            v => clock.DateFontFamily = v,
            placeholder: SameAsTime));
        rows.AddRow("Size", Spinner(5, 100, (int)Math.Round(clock.DateFontScale * 100),
            v => { clock.DateFontScale = v / 100f; ApplyClock(); }, "% of time"));
        rows.AddRow("Minimum size", Spinner(6, 200, (int)clock.DateMinFontSize,
            v => { clock.DateMinFontSize = v; ApplyClock(); }, "px"));
        rows.AddRow("Colour", BuildDateColorPicker());
        rows.AddRow("Opacity", Spinner(10, 100, (int)Math.Round(clock.DateOpacity * 100),
            v => { clock.DateOpacity = v / 100f; ApplyClock(); }, "% of time colour"));

        return card;
    }

    /// <summary>Unlike the time's picker this one can be cleared back to "inherit", which is the
    /// default, so it carries a reset rather than only a colour dialog.</summary>
    private Control BuildDateColorPicker()
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Colors.Card,
            Margin = new Padding(0, 2, 0, 2),
        };

        var swatch = new Button
        {
            Width = 58,
            Height = 26,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 10, 0),
        };
        swatch.FlatAppearance.BorderColor = Theme.Colors.Border;

        var label = new Label
        {
            AutoSize = true,
            ForeColor = Theme.Colors.Subtle,
            Margin = new Padding(0, 5, 10, 0),
            Font = new Font("Consolas", 9f),
        };

        var reset = new Button
        {
            Text = "Inherit",
            Width = 66,
            Height = 26,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Colors.Control,
            ForeColor = Theme.Colors.Text,
            Margin = new Padding(0, 0, 0, 0),
        };
        reset.FlatAppearance.BorderColor = Theme.Colors.Border;

        void Sync()
        {
            bool inherited = string.IsNullOrWhiteSpace(_config.Clock.DateColor);
            var effective = Widgets.ClockRenderer.DateColorFor(_config.Clock, Widgets.ClockRenderer.ParseColor(_config.Clock.Color));
            swatch.BackColor = Color.FromArgb(255, effective.R, effective.G, effective.B);
            label.Text = inherited ? "inherited" : _config.Clock.DateColor!.ToUpperInvariant();
            reset.Enabled = !inherited;
        }

        swatch.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = swatch.BackColor, FullOpen = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var c = dialog.Color;
            // The date's own alpha, not the time's. An inherited date is timeColor.A * DateOpacity
            // and an explicit one carries its own; CurrentAlpha() returns neither, so picking a new
            // RGB used to silently reset the date's opacity to the time's.
            byte alpha = Widgets.ClockRenderer.DateColorFor(
                _config.Clock, Widgets.ClockRenderer.ParseColor(_config.Clock.Color)).A;
            _config.Clock.DateColor = $"#{alpha:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            Sync();
            ApplyClock();
        };

        reset.Click += (_, _) =>
        {
            _config.Clock.DateColor = null;
            Sync();
            ApplyClock();
        };

        row.Controls.Add(swatch);
        row.Controls.Add(label);
        row.Controls.Add(reset);
        Sync();
        return row;
    }

    private const string SameAsTime = "(same as time)";

    /// <summary>The info widget's page. Off by default and off in a fresh config, so this is
    /// where it gets turned on — the feature is meant to need no JSON editing at all.
    ///
    /// The now-playing line is worth a word of warning here rather than in the docs: it shows
    /// whatever has a media session, a browser tab included, on a desktop other people can see.</summary>
    private Control BuildInfoSection()
    {
        var (card, rows) = NewSection("Info widget");
        var info = _config.Info;

        rows.AddRow("Show info widget", Check(info.Enabled, v => { info.Enabled = v; ApplyInfo(); }));
        rows.AddRow("Now playing", Check(HasSource("nowPlaying"), v => { SetSource("nowPlaying", v); ApplyInfo(); }));
        rows.AddRow("Battery", Check(HasSource("battery"), v => { SetSource("battery", v); ApplyInfo(); }));
        rows.AddRow("Order", Choice(["Now playing first", "Battery first"], NowPlayingIsFirst() ? 0 : 1,
            i => { SetOrder(nowPlayingFirst: i == 0); ApplyInfo(); }));
        rows.AddRow("Max characters", Spinner(10, 200, info.MaxCharacters,
            v => { info.MaxCharacters = v; ApplyInfo(); }, "chars"));
        rows.AddRow("Drop shadow", Check(info.Shadow, v => { info.Shadow = v; ApplyInfo(); }));
        rows.AddRow("Position", BuildAnchorPicker(() => info.Anchor, v => info.Anchor = v, ApplyInfo));
        rows.AddRow("Margin X", Spinner(0, 2000, info.MarginX, v => { info.MarginX = v; ApplyInfo(); }, "px"));
        rows.AddRow("Margin Y", Spinner(0, 2000, info.MarginY, v => { info.MarginY = v; ApplyInfo(); }, "px"));
        rows.AddRow("Font size", Spinner(10, 200, (int)info.FontSize, v => { info.FontSize = v; ApplyInfo(); }, "px"));
        rows.AddRow("Font", BuildFontPicker(() => info.FontFamily, v => info.FontFamily = v, apply: ApplyInfo));
        rows.AddRow("Colour", BuildInfoColorPicker());
        rows.AddRow("Opacity", Spinner(10, 100, (int)Math.Round(InfoAlpha() / 2.55), v =>
        {
            SetInfoColor(InfoRgb(), (int)Math.Round(v * 2.55));
            if (_infoColorHex is not null) _infoColorHex.Text = _config.Info.Color.ToUpperInvariant();
            ApplyInfo();
        }, "%"));

        return card;
    }

    // ---- battery halo -------------------------------------------------------------------

    /// <summary>Palette presets. Values, not code paths — selecting one fills the pickers in and
    /// they stay editable afterwards, because a preset is a starting point rather than a lock.
    /// Order: low, mid, high, charged, track.</summary>
    private static readonly Dictionary<string, string[]> Palettes = new()
    {
        ["Ember"] = ["#FF4D4D", "#FF9A3C", "#FFD166", "#FFF3B0", "#24FFDCB4"],
        ["Default"] = ["#FF6B5B", "#F5C451", "#6FE39F", "#5BC8FF", "#24FFFFFF"],
        ["Monochrome"] = ["#8A8F98", "#B9BFC9", "#EDF1F7", "#FFFFFF", "#20FFFFFF"],
    };

    private readonly List<Control> _haloAttachedOnly = [];
    private readonly List<Control> _haloDetachedOnly = [];
    private readonly List<Action> _haloSwatchRefresh = [];
    private ComboBox? _paletteCombo;

    /// <summary>The halo gets its own page rather than a group on the Info page: that page already
    /// carries twelve rows and this adds fourteen, which is past the point where a settings page
    /// stops being readable. Same reason the clock and the date are separate pages.</summary>
    private Control BuildHaloSection()
    {
        var (card, rows) = NewSection("Battery halo");
        var halo = _config.Info.Halo;

        rows.AddRow("Show halo", Check(halo.Enabled, v => { halo.Enabled = v; ApplyInfo(); }));
        rows.AddRow("Size", Spinner(12, 200, halo.Size, v => { halo.Size = v; ApplyInfo(); }, "px"));

        _paletteCombo = Choice([.. Palettes.Keys, "Custom"], PaletteIndexFor(halo), i =>
        {
            var names = Palettes.Keys.ToArray();
            if (i >= names.Length) return;          // "Custom" is a readout, not an action
            var preset = Palettes[names[i]];
            halo.LowColor = preset[0];
            halo.MidColor = preset[1];
            halo.HighColor = preset[2];
            halo.ChargedColor = preset[3];
            halo.TrackColor = preset[4];
            foreach (var refresh in _haloSwatchRefresh) refresh();
            ApplyInfo();
        });
        rows.AddRow("Palette", _paletteCombo);

        rows.AddRow("Low colour", BuildHaloColorPicker(() => halo.LowColor, v => halo.LowColor = v));
        rows.AddRow("Mid colour", BuildHaloColorPicker(() => halo.MidColor, v => halo.MidColor = v));
        rows.AddRow("High colour", BuildHaloColorPicker(() => halo.HighColor, v => halo.HighColor = v));
        rows.AddRow("Charged colour", BuildHaloColorPicker(() => halo.ChargedColor, v => halo.ChargedColor = v));
        rows.AddRow("Track colour", BuildHaloColorPicker(() => halo.TrackColor, v => halo.TrackColor = v));

        rows.AddRow("Colour by level", Check(halo.ColorByLevel, v => { halo.ColorByLevel = v; ApplyInfo(); }));
        rows.AddRow("Low below", Spinner(1, 99, halo.LowThreshold, v => { halo.LowThreshold = v; ApplyInfo(); }, "%"));
        rows.AddRow("Mid below", Spinner(1, 99, halo.MidThreshold, v => { halo.MidThreshold = v; ApplyInfo(); }, "%"));

        rows.AddRow("Detach from text", Check(halo.Detached, v =>
        {
            halo.Detached = v;
            UpdateHaloPlacementControls();
            ApplyInfo();
        }));

        var placement = Choice(["Left", "Right", "Above", "Below"], (int)halo.Placement,
            i => { halo.Placement = (HaloPlacement)i; ApplyInfo(); });
        _haloAttachedOnly.Add(placement);
        rows.AddRow("Beside text", placement);

        var anchor = BuildAnchorPicker(() => halo.Anchor, v => halo.Anchor = v, ApplyInfo);
        var marginX = Spinner(0, 2000, halo.MarginX, v => { halo.MarginX = v; ApplyInfo(); }, "px");
        var marginY = Spinner(0, 2000, halo.MarginY, v => { halo.MarginY = v; ApplyInfo(); }, "px");
        _haloDetachedOnly.AddRange([anchor, marginX, marginY]);
        rows.AddRow("Position", anchor);
        rows.AddRow("Margin X", marginX);
        rows.AddRow("Margin Y", marginY);

        UpdateHaloPlacementControls();
        return card;
    }

    /// <summary>Only the controls that apply are live. An enabled Placement picker in detached mode
    /// would silently ignore every click, which is worse than one that is visibly off.</summary>
    private void UpdateHaloPlacementControls()
    {
        bool detached = _config.Info.Halo.Detached;
        foreach (var c in _haloAttachedOnly) SetLive(c, !detached);
        foreach (var c in _haloDetachedOnly) SetLive(c, detached);
    }

    /// <summary>Disabling a container stops its children responding, and dims the ones that honour
    /// ForeColor — the spinners and the combo.
    ///
    /// The anchor grid is the exception and it is not fixed here: a RadioButton with
    /// Appearance.Button and FlatStyle.Flat is painted by WinForms with the system disabled colour
    /// regardless of what ForeColor says, so it stays looking live while ignoring every click.
    /// Inert but ambiguous. Fixing it properly means owner-drawing the grid, which is more change
    /// than a greyed-out arrow is worth.</summary>
    private static void SetLive(Control control, bool live)
    {
        control.Enabled = live;
        foreach (Control child in control.Controls)
            child.ForeColor = live ? Theme.Colors.Text : Theme.Colors.Subtle;
    }

    /// <summary>Which preset the current colours match, or "Custom". Editing any picker moves the
    /// dropdown here without touching the colours.</summary>
    private int PaletteIndexFor(HaloConfig halo)
    {
        string[] current = [halo.LowColor, halo.MidColor, halo.HighColor, halo.ChargedColor, halo.TrackColor];
        int index = 0;
        foreach (var preset in Palettes.Values)
        {
            if (preset.SequenceEqual(current, StringComparer.OrdinalIgnoreCase)) return index;
            index++;
        }
        return Palettes.Count;   // Custom
    }

    private Control BuildHaloColorPicker(Func<string> get, Action<string> set)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Colors.Card,
            Margin = new Padding(0, 2, 0, 2),
        };

        var button = new Button { Width = 58, Height = 26, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 10, 0) };
        button.FlatAppearance.BorderColor = Theme.Colors.Border;

        var hex = new Label
        {
            AutoSize = true,
            ForeColor = Theme.Colors.Subtle,
            Margin = new Padding(0, 5, 0, 0),
            Font = new Font("Consolas", 9f),
            BackColor = Theme.Colors.Card,
        };

        void Refresh()
        {
            var current = ClockRenderer.ParseColor(get());
            button.BackColor = Color.FromArgb(current.R, current.G, current.B);
            hex.Text = get().ToUpperInvariant();
        }

        Refresh();
        _haloSwatchRefresh.Add(Refresh);

        button.Click += (_, _) =>
        {
            var existing = ClockRenderer.ParseColor(get());
            using var dialog = new ColorDialog
            {
                Color = Color.FromArgb(existing.R, existing.G, existing.B),
                FullOpen = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            // Alpha carries over from the value being replaced. The track colour is deliberately
            // translucent and a colour dialog has no way to express that, so picking a new hue
            // must not quietly make it opaque.
            set($"#{existing.A:X2}{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
            Refresh();
            SyncPaletteCombo();
            ApplyInfo();
        };

        row.Controls.Add(button);
        row.Controls.Add(hex);
        return row;
    }

    /// <summary>Moves the dropdown to whichever preset now matches, or to Custom. Called after a
    /// picker edit, never as part of applying a preset — that would fight the selection.</summary>
    private void SyncPaletteCombo()
    {
        if (_paletteCombo is null) return;
        _paletteCombo.SelectedIndex = PaletteIndexFor(_config.Info.Halo);
    }

    private bool HasSource(string name) =>
        _config.Info.Sources.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adding appends, so turning a source back on does not silently reorder the other
    /// one. The Order row is what moves them.</summary>
    private void SetSource(string name, bool on)
    {
        if (on)
        {
            if (!HasSource(name)) _config.Info.Sources.Add(name);
        }
        else
        {
            _config.Info.Sources.RemoveAll(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    private bool NowPlayingIsFirst()
    {
        int now = _config.Info.Sources.FindIndex(s => string.Equals(s, "nowPlaying", StringComparison.OrdinalIgnoreCase));
        int battery = _config.Info.Sources.FindIndex(s => string.Equals(s, "battery", StringComparison.OrdinalIgnoreCase));
        if (now < 0) return false;
        if (battery < 0) return true;
        return now < battery;
    }

    /// <summary>Reorders without adding: a source the user switched off stays off.</summary>
    private void SetOrder(bool nowPlayingFirst)
    {
        var ordered = new List<string>();
        foreach (var name in nowPlayingFirst ? new[] { "nowPlaying", "battery" } : ["battery", "nowPlaying"])
            if (HasSource(name)) ordered.Add(name);
        // Anything a later version wrote is preserved rather than dropped on the floor.
        ordered.AddRange(_config.Info.Sources.Where(s =>
            !string.Equals(s, "nowPlaying", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(s, "battery", StringComparison.OrdinalIgnoreCase)));
        _config.Info.Sources = ordered;
    }

    private Color InfoColor() => ClockRenderer.ParseColor(_config.Info.Color);
    /// <summary>The colour without its alpha — the swatch and the dialog work in RGB, and the
    /// opacity spinner owns the alpha channel separately.</summary>
    private Color InfoRgb()
    {
        var c = InfoColor();
        return Color.FromArgb(c.R, c.G, c.B);
    }
    private int InfoAlpha() => InfoColor().A;

    private void SetInfoColor(Color rgb, int alpha) =>
        _config.Info.Color = $"#{alpha:X2}{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";

    /// <summary>Its own button rather than a parameterised BuildColorPicker: that one owns two
    /// form fields bound to the clock, and threading a second target through them would make the
    /// clock's picker harder to follow for no gain here.</summary>
    private Control BuildInfoColorPicker()
    {
        var button = new Button
        {
            Width = 58,
            Height = 26,
            BackColor = InfoRgb(),
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 10, 0),
        };
        button.FlatAppearance.BorderColor = Theme.Colors.Border;

        var hex = new Label
        {
            AutoSize = true,
            Text = _config.Info.Color.ToUpperInvariant(),
            ForeColor = Theme.Colors.Subtle,
            Margin = new Padding(0, 5, 0, 0),
            Font = new Font("Consolas", 9f),
            BackColor = Theme.Colors.Card,
        };

        button.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = InfoRgb(), FullOpen = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            SetInfoColor(dialog.Color, InfoAlpha());
            button.BackColor = dialog.Color;
            hex.Text = _config.Info.Color.ToUpperInvariant();
            ApplyInfo();
        };

        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Colors.Card,
            Margin = new Padding(0, 2, 0, 2),
        };
        row.Controls.Add(button);
        row.Controls.Add(hex);
        _infoColorHex = hex;
        return row;
    }

    /// <summary>Kept so the opacity spinner can refresh the hex, which carries the alpha byte.</summary>
    private Label? _infoColorHex;

    /// <summary>The info widget has no preview surface of its own, so unlike ApplyClock this
    /// only persists and rebuilds — what it looks like is checked on the desktop.</summary>
    private void ApplyInfo()
    {
        if (_loading) return;
        _engine.RefreshWidgets();
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

    private Control BuildAnchorPicker() =>
        BuildAnchorPicker(() => _config.Clock.Anchor, v => _config.Clock.Anchor = v, ApplyClock);

    /// <summary>Anchor grid over an arbitrary property, so the clock and the info widget can
    /// each be positioned independently.</summary>
    private Control BuildAnchorPicker(Func<ClockAnchor> get, Action<ClockAnchor> set, Action apply)
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
                Checked = get() == anchor,
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
                set((ClockAnchor)cell.Tag!);
                apply();
            };
            grid.Controls.Add(cell);
        }
        return grid;
    }

    /// <summary>Every family is drawn in its own face, so you pick a font by looking at it.</summary>
    private Control BuildFontPicker() => BuildFontPicker(() => _config.Clock.FontFamily, v => _config.Clock.FontFamily = v);

    /// <summary>Font picker over an arbitrary property, so the time and the date can each have
    /// one. <paramref name="placeholder"/> is shown when the value is empty, which for the date
    /// means "whatever the time is using".</summary>
    /// <summary><paramref name="apply"/> defaults to the clock's refresh because most callers are
    /// clock pages. The info widget must pass its own — since the clock and info refresh paths
    /// split, ApplyClock no longer rebuilds the info overlay.</summary>
    private Control BuildFontPicker(Func<string?> get, Action<string> set, string? placeholder = null, Action? apply = null)
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
        if (placeholder is not null) combo.Items.Insert(0, placeholder);

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

        string current = get() ?? "";
        combo.Text = current.Length == 0 && placeholder is not null ? placeholder : current;
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || combo.SelectedItem is not string family) return;
            set(family == placeholder ? "" : family);
            (apply ?? ApplyClock)();
        };
        combo.Leave += (_, _) =>
        {
            if (_loading) return;
            if (combo.Text.Length == 0)
            {
                // Only the date picker has a placeholder, and there an erased box is a real choice
                // — "inherit the time font" — which the early return used to discard, leaving the
                // old family set and the inheritance unreachable from the UI. The time picker has
                // no such state, so a blank there is still treated as a half-finished edit.
                if (placeholder is null) return;
                set("");
                (apply ?? ApplyClock)();
                return;
            }
            set(combo.Text == placeholder ? "" : combo.Text);
            (apply ?? ApplyClock)();
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
