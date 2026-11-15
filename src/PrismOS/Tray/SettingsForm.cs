using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using PrismOS.Config;
using PrismOS.Interop;
using PrismOS.Widgets;
using PrismOS.Widgets.Themes;

namespace PrismOS.Tray;

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
    private const int ContentWidth = 640;
    private const int RailWidth = 140;
    private const int TabHeight = 38;
    private const int FooterHeight = 58;

    private readonly Engine _engine;
    private readonly AppConfig _config;

    /// <summary>Whether this machine has a battery. When it does not, the Battery page and the
    /// battery source rows do not appear — a desktop would show nothing on them forever.</summary>
    private readonly bool _batteryAvailable;
    private readonly ClockPreview _preview;
    private bool _loading = true;
    private readonly BackdropHolder _backdrop = new();
    /// <summary>The DPI the layout sizes in this file are written for; WinForms rescales them when the
    /// window moves monitors, but a control built afterwards has to be scaled by hand.</summary>
    private int _layoutDpi;

    /// <summary>The accent the themes use, read once and refreshed when the Style page changes it,
    /// so the once-a-second preview does not ask the engine every tick.</summary>
    private Color _accent;
    private string _accentSource = "default";
    private readonly List<(Label Caption, string Text)> _classicOnly = [];

    /// <summary>Per-widget position rows. A group theme places the whole set from the Style page,
    /// so these say so while one is active.</summary>
    private readonly List<(Label Caption, string Text)> _placement = [];
    private readonly List<Control> _groupRows = [];
    private Panel? _groupPositionHost;
    private Control? _themeSize;
    private Label? _themeSizeCaption;

    /// <summary>The clock's font size also sizes the themes until the Style page's Size is set;
    /// after that it is Classic's alone, and says so.</summary>
    private Label? _clockFontSizeCaption;
    private readonly List<ThemeTile> _tiles = [];
    private Label? _styleHint;
    private Button? _accentSwatch;
    private Label? _accentHex;

    // ---- wallpaper page -------------------------------------------------------------------
    /// <summary>The preview of what is on the selected display.</summary>
    private WallpaperPreview? _wallpaperPreview;
    /// <summary>The device each entry in the display list points at: "*" for all displays, or a
    /// monitor device name. The list is rebuilt when the page is built, so a display that was
    /// plugged in afterwards is picked up the next time the panel opens.</summary>
    private readonly List<string> _wallpaperTargets = [];
    private int _wallpaperTargetIndex;
    private Panel? _wallpaperCreditHost;
    private Label? _wallpaperCreditCaption;

    private Button _colorButton = null!;
    private Label _colorHex = null!;

    public SettingsForm(Engine engine)
    {
        _engine = engine;
        _config = engine.Config;
        _batteryAvailable = engine.BatteryAvailable;
        (_accent, _accentSource) = engine.CurrentAccent();
        engine.AccentChanged += OnAccentChanged;
        engine.WidgetsChanged += OnWidgetsChanged;
        engine.WallpaperChanged += OnWallpaperChanged;
        _preview = new ClockPreview(_config.Clock, CurrentStyle);

        Text = "PrismOS";
        Icon = PrismIcon.CreateIcon();
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        _layoutDpi = DeviceDpi;
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
        _pagesHost = pages;
        _rail = rail;

        BuildPages(pages, rail);
        RefreshStyle();

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
    private Panel? _pagesHost;
    private Panel? _rail;

    /// <summary>Builds every page and its tab.
    ///
    /// A method rather than a block in the constructor because the panel has to be rebuilt after
    /// a reset: the controls hold the values they were built with, so a panel that survived a
    /// reset would show the old settings over the new ones.</summary>
    private void BuildPages(Panel host, Panel rail)
    {
        foreach (var (tab, page) in _pages)
        {
            tab.Dispose();
            page.Dispose();
        }
        _pages.Clear();
        _tiles.Clear();
        _classicOnly.Clear();
        _placement.Clear();
        _groupRows.Clear();
        // Disposing a control removes it from its parent's collection, so the rail is walked over
        // a copy: enumerating it directly would throw on the second removal.
        foreach (Control control in rail.Controls.Cast<Control>().ToArray()) control.Dispose();
        rail.Controls.Clear();

        AddPage(host, rail, "Style", BuildStyleSection());
        AddPage(host, rail, "Clock", BuildClockSection());
        AddPage(host, rail, "Date", BuildDateSection());
        AddPage(host, rail, "Info", BuildInfoSection());
        if (_batteryAvailable) AddPage(host, rail, "Battery", BuildHaloSection());
        AddPage(host, rail, "Music", BuildDiscSection());
        AddPage(host, rail, "Wallpaper", BuildWallpaperSection());
        AddPage(host, rail, "Behaviour", BuildBehaviorSection());
        AddPage(host, rail, "About", BuildAboutSection());
        SelectPage(0);
    }

    /// <summary>Rebuilds the panel from the config, for after a reset.</summary>
    private void RebuildPages()
    {
        if (_pagesHost is null || _rail is null) return;
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            BuildPages(_pagesHost, _rail);
            RefreshStyle();
        }
        finally
        {
            _loading = wasLoading;
        }
        ResizeToActivePage();
    }

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
            // From the form's font, which WinForms rescales per monitor; a fresh 9.5 pt font is sized
            // for the primary display, so on a 100% monitor the tabs grew 1.5x on the first click.
            _pages[i].Tab.Font = new Font(Font, active ? FontStyle.Bold : FontStyle.Regular);
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
        // The wallpaper may have changed while this panel was closed.
        RefreshWallpaperSection();
        CenterToScreen();
        // Spawned from the tray, our process may not hold foreground rights — force it.
        TopMost = true;
        Activate();
        BringToFront();
        TopMost = false;
    }

    // ---- sections ----------------------------------------------------------------------

    // ---- style ---------------------------------------------------------------------------

    private WidgetStyle CurrentStyle() =>
        new(_config.Widgets.ResolveTheme(), _accent, _config.Widgets.ScaleFor(_config.Clock));

    /// <summary>First in the rail because it is the most visible choice. Every tile is drawn by
    /// its theme's own renderers with sample data, so what is shown is what lands on the desktop.</summary>
    private Control BuildStyleSection()
    {
        var (card, rows) = NewSection("Widget style");

        var grid = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            MaximumSize = new Size(ContentWidth - 32, 0),
            BackColor = Theme.Colors.Card,
            Margin = new Padding(0, 0, 0, 10),
        };
        _backdrop.Replace(LoadBackdrop());
        foreach (var theme in Enum.GetValues<WidgetTheme>())
        {
            var tile = new ThemeTile(theme, (t, size) => ThemePreview.Render(t, size, _accent, _backdrop.Current, _config, _batteryAvailable))
            {
                Size = new Size(192, 132),
                Margin = new Padding(0, 0, 10, 10),
            };
            tile.Click += (_, _) =>
            {
                if (_config.Widgets.ResolveTheme() == tile.Style) return;
                _engine.SetWidgetTheme(tile.Style);
            };
            _tiles.Add(tile);
            grid.Controls.Add(tile);
        }
        card.Controls.Add(grid);
        card.Controls.SetChildIndex(grid, 1);

        rows.AddRow("Accent", BuildAccentPicker());

        // Every theme but Classic; Classic keeps its own font size on the Clock page.
        _themeSize = Spinner(50, 300, (int)Math.Round(_config.Widgets.ScaleFor(_config.Clock) * 100), v =>
        {
            if (_loading) return;
            _config.Widgets.Size = v;
            _engine.RefreshWidgets();
            foreach (var tile in _tiles) tile.Rerender();
            RefreshClassicOnly();
            _preview.Invalidate();
        }, "%");
        _themeSizeCaption = rows.AddRowLabel("Size", _themeSize);

        // The group themes place the whole set at one position on one monitor.
        _groupPositionHost = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Colors.Card,
            Margin = new Padding(0),
        };
        rows.AddGroupOnly("Position", _groupPositionHost);
        rows.AddGroupOnly("Monitor", BuildGroupMonitorChoice());

        _styleHint = new Label
        {
            Text = "Turn on the Info widget, with Now playing and Battery, to see the full set.",
            AutoSize = true,
            MaximumSize = new Size(ContentWidth - 32, 0),
            ForeColor = Theme.Colors.Subtle,
            Margin = new Padding(0, 8, 0, 0),
        };
        card.Controls.Add(_styleHint);
        return card;
    }

    /// <summary>The wallpaper's own frame behind the tiles when there is one. Copied out of the
    /// file so the engine can keep rewriting it while the window is open.</summary>
    private Image? LoadBackdrop()
    {
        try
        {
            if (_engine.PreviewBackdropPath() is not { } path) return null;
            using var stream = File.OpenRead(path);
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch (Exception ex)
        {
            Common.Log.Warn($"Style page backdrop unavailable: {ex.Message}");
            return null;
        }
    }

    private Control BuildGroupMonitorChoice()
    {
        var monitors = Desktop.MonitorTracker.Enumerate();
        var items = new List<string> { "Primary" };
        items.AddRange(monitors.Select(m => $"{m.Device.Replace(@"\\.\", "")}  {m.Bounds.Width}×{m.Bounds.Height}"));
        int selected = 0;
        for (int i = 0; i < monitors.Count; i++)
            if (string.Equals(monitors[i].Device, _config.Widgets.Monitor, StringComparison.OrdinalIgnoreCase)) selected = i + 1;
        return Choice([.. items], selected, i =>
        {
            if (_loading) return;
            _config.Widgets.Monitor = i == 0 ? "*" : monitors[i - 1].Device;
            _engine.RefreshWidgets();
        });
    }

    /// <summary>Rebuilt on every theme change, so a theme with no stored position shows its own
    /// default — Orbit's and Monolith's top left, Poster's top right, Glass dock's bottom, Tiles'
    /// bottom right.</summary>
    private void RebuildGroupPosition(WidgetTheme theme)
    {
        if (_groupPositionHost is null) return;
        ControlTree.ClearAndDispose(_groupPositionHost);
        if (!WidgetsConfig.IsGroup(theme)) return;
        var picker = BuildAnchorPicker(
            () => _config.Widgets.Position ?? GroupThemes.For(theme).DefaultPosition,
            v => _config.Widgets.Position = v,
            () => { if (!_loading) _engine.RefreshWidgets(); });
        if (IsHandleCreated && DeviceDpi != _layoutDpi)
            picker.Scale(new SizeF((float)DeviceDpi / _layoutDpi, (float)DeviceDpi / _layoutDpi));
        _groupPositionHost.Controls.Add(picker);
    }

    private Control BuildAccentPicker()
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Colors.Card,
            Margin = new Padding(0, 2, 0, 2),
        };
        _accentSwatch = new Button { Width = 58, Height = 26, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 10, 0) };
        _accentSwatch.FlatAppearance.BorderColor = Theme.Colors.Border;
        _accentHex = new Label
        {
            AutoSize = true,
            ForeColor = Theme.Colors.Subtle,
            Margin = new Padding(0, 5, 10, 0),
            Font = new Font("Consolas", 9f),
        };
        var auto = new Button
        {
            Text = "Auto",
            Width = 66,
            Height = 26,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Colors.Control,
            ForeColor = Theme.Colors.Text,
        };
        auto.FlatAppearance.BorderColor = Theme.Colors.Border;

        _accentSwatch.Click += (_, _) =>
        {
            // The accent is #RRGGBB and nothing else: Accent.Resolve matches it against a
            // six-digit pattern and quietly falls back to the wallpaper's colour for anything
            // longer, so the picker is opened without an alpha to choose.
            if (PickColor(_accent, allowAlpha: false) is not { } c) return;
            _engine.SetWidgetAccent(ColorHex.ToRgbHex(c));
        };
        auto.Click += (_, _) =>
        {
            _engine.SetWidgetAccent("auto");
        };

        row.Controls.Add(_accentSwatch);
        row.Controls.Add(_accentHex);
        row.Controls.Add(auto);
        return row;
    }

    /// <summary>After any theme or accent change: tiles, swatch, labels, preview and hint.</summary>
    private void RefreshStyle()
    {
        var before = _accent;
        (_accent, _accentSource) = _engine.CurrentAccent();
        var theme = _config.Widgets.ResolveTheme();
        foreach (var tile in _tiles)
        {
            tile.Selected = tile.Style == theme;
            if (_accent != before) tile.Rerender();
        }
        if (_accentSwatch is not null) _accentSwatch.BackColor = _accent;
        if (_accentHex is not null)
            _accentHex.Text = $"#{_accent.R:X2}{_accent.G:X2}{_accent.B:X2}  {_accentSource}";
        bool group = WidgetsConfig.IsGroup(theme);
        foreach (var control in _groupRows) control.Visible = group;
        RebuildGroupPosition(theme);
        if (_styleHint is not null)
            _styleHint.Visible = theme != WidgetTheme.Classic
                && !(_config.Info.Enabled && HasSource("nowPlaying") && HasSource("battery"));
        RefreshClassicOnly();
        _preview.Invalidate();
        ResizeToActivePage();
    }

    /// <summary>A new accent means a new wallpaper frame, so the tiles get the new backdrop too.</summary>
    private void OnAccentChanged()
    {
        if (IsDisposed) return;
        // One failed read keeps the frame already shown rather than greying every tile.
        if (LoadBackdrop() is { } fresh) _backdrop.Replace(fresh);
        foreach (var tile in _tiles) tile.Rerender();
        RefreshStyle();
    }

    /// <summary>A style, accent or size chosen from the tray while this window is open.</summary>
    private void OnWidgetsChanged()
    {
        if (IsDisposed) return;
        if (_themeSize?.Controls.OfType<NumericUpDown>().FirstOrDefault() is { } size)
        {
            bool was = _loading;
            _loading = true;
            size.Value = Math.Clamp((int)Math.Round(_config.Widgets.ScaleFor(_config.Clock) * 100), (int)size.Minimum, (int)size.Maximum);
            _loading = was;
        }
        foreach (var tile in _tiles) tile.Rerender();
        RefreshStyle();
    }

    /// <summary>The wallpaper changed from the tray or the gallery while this window was open:
    /// the preview shows what is there now, not what was there when the panel opened.</summary>
    private void OnWallpaperChanged()
    {
        if (IsDisposed) return;
        RefreshWallpaperSection();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _engine.AccentChanged -= OnAccentChanged;
        _engine.WidgetsChanged -= OnWidgetsChanged;
        _engine.WallpaperChanged -= OnWallpaperChanged;
        _backdrop.Dispose();
        base.OnFormClosed(e);
    }

    /// <summary>Marks every Classic-only setting while another theme is active, and unmarks them
    /// when Classic is back.</summary>
    private void RefreshClassicOnly()
    {
        var theme = _config.Widgets.ResolveTheme();
        bool other = theme != WidgetTheme.Classic;
        foreach (var (caption, text) in _classicOnly)
        {
            caption.Text = other ? $"{text}  · Classic only" : text;
            caption.ForeColor = other ? Theme.Colors.Subtle : Theme.Colors.Text;
        }
        if (_clockFontSizeCaption is not null)
        {
            bool classicOnly = other && _config.Widgets.Size.HasValue;
            _clockFontSizeCaption.Text = classicOnly ? "Font size  · Classic only" : "Font size";
            _clockFontSizeCaption.ForeColor = classicOnly ? Theme.Colors.Subtle : Theme.Colors.Text;
        }
        if (_themeSize is not null && _themeSizeCaption is not null)
            _themeSize.Visible = _themeSizeCaption.Visible = other;
        bool group = WidgetsConfig.IsGroup(theme);
        foreach (var (caption, text) in _placement)
        {
            caption.Text = group ? $"{text}  · set on Style page" : text;
            caption.ForeColor = group ? Theme.Colors.Subtle : Theme.Colors.Text;
        }
    }

    private Control BuildClockSection()
    {
        var (card, rows) = NewSection("Clock");

        rows.AddRow("Show clock", Check(_config.Clock.Enabled, v => { _config.Clock.Enabled = v; ApplyClock(); }));
        rows.AddRow("Time format", Choice(["12-hour", "24-hour"], _config.Clock.TwentyFourHour ? 1 : 0,
            i => { _config.Clock.TwentyFourHour = i == 1; ApplyClock(); }));
        rows.AddRow("Show seconds", Check(_config.Clock.ShowSeconds, v => { _config.Clock.ShowSeconds = v; ApplyClock(); }));
        // "Show date" and "Separator rule" live on the Date page — two controls bound to one
        // property desync the moment either is used.
        rows.AddClassicOnly("Drop shadow", Check(_config.Clock.Shadow, v => { _config.Clock.Shadow = v; ApplyClock(); }));
        rows.AddPlacement("Position", BuildAnchorPicker());
        rows.AddPlacement("Margin X", Spinner(0, 2000, _config.Clock.MarginX, v => { _config.Clock.MarginX = v; ApplyClock(); }, "px"));
        rows.AddPlacement("Margin Y", Spinner(0, 2000, _config.Clock.MarginY, v => { _config.Clock.MarginY = v; ApplyClock(); }, "px"));
        _clockFontSizeCaption = rows.AddRowLabel("Font size", Spinner(24, 400, (int)_config.Clock.FontSize, v => { _config.Clock.FontSize = v; ApplyClock(); }, "px"));
        rows.AddClassicOnly("Font", BuildFontPicker());
        rows.AddClassicOnly("Colour", BuildColorPicker());
        rows.AddClassicOnly("Opacity", Spinner(10, 100, (int)Math.Round(CurrentAlpha() / 2.55), v =>
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
        rows.AddClassicOnly("Separator rule", Check(clock.Separator, v => { clock.Separator = v; ApplyClock(); }));
        rows.AddClassicOnly("Font", BuildFontPicker(
            () => clock.DateFontFamily,
            v => clock.DateFontFamily = v,
            placeholder: SameAsTime));
        rows.AddClassicOnly("Size", Spinner(5, 100, (int)Math.Round(clock.DateFontScale * 100),
            v => { clock.DateFontScale = v / 100f; ApplyClock(); }, "% of time"));
        rows.AddClassicOnly("Minimum size", Spinner(6, 200, (int)clock.DateMinFontSize,
            v => { clock.DateMinFontSize = v; ApplyClock(); }, "px"));
        rows.AddClassicOnly("Colour", BuildDateColorPicker());
        rows.AddClassicOnly("Opacity", Spinner(10, 100, (int)Math.Round(clock.DateOpacity * 100),
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
            // Started from the colour as it is drawn, alpha included, so the opacity a person
            // sees is the opacity they keep. The date's own alpha, not the time's: an inherited
            // date is timeColor.A * DateOpacity and an explicit one carries its own.
            var current = Widgets.ClockRenderer.DateColorFor(
                _config.Clock, Widgets.ClockRenderer.ParseColor(_config.Clock.Color));
            if (PickColor(current, allowAlpha: true) is not { } picked) return;
            _config.Clock.DateColor = ColorHex.ToHex(picked);
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

        if (_batteryAvailable)
        {
            rows.AddRow("Battery", Check(HasSource("battery"), v => { SetSource("battery", v); ApplyInfo(); }));
            rows.AddRow("Order", Choice(["Now playing first", "Battery first"], NowPlayingIsFirst() ? 0 : 1,
                i => { SetOrder(nowPlayingFirst: i == 0); ApplyInfo(); }));
        }
        rows.AddRow("Max characters", Spinner(10, 200, info.MaxCharacters,
            v => { info.MaxCharacters = v; ApplyInfo(); }, "chars"));
        rows.AddClassicOnly("Drop shadow", Check(info.Shadow, v => { info.Shadow = v; ApplyInfo(); }));
        rows.AddPlacement("Position", BuildAnchorPicker(() => info.Anchor, v => info.Anchor = v, ApplyInfo));
        rows.AddPlacement("Margin X", Spinner(0, 2000, info.MarginX, v => { info.MarginX = v; ApplyInfo(); }, "px"));
        rows.AddPlacement("Margin Y", Spinner(0, 2000, info.MarginY, v => { info.MarginY = v; ApplyInfo(); }, "px"));
        rows.AddClassicOnly("Font size", Spinner(10, 200, (int)info.FontSize, v => { info.FontSize = v; ApplyInfo(); }, "px"));
        rows.AddClassicOnly("Font", BuildFontPicker(() => info.FontFamily, v => info.FontFamily = v, apply: ApplyInfo));
        rows.AddClassicOnly("Colour", BuildInfoColorPicker());
        rows.AddClassicOnly("Opacity", Spinner(10, 100, (int)Math.Round(InfoAlpha() / 2.55), v =>
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
        // First entry must match HaloConfig's defaults, or a fresh install opens on "Custom".
        ["Signal"] = ["#FF4D4D", "#FF9A3C", "#5FD98A", "#7CE8A4", "#24FFFFFF"],
        ["Ember"] = ["#FF4D4D", "#FF9A3C", "#FFD166", "#FFF3B0", "#24FFDCB4"],
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
        rows.AddClassicOnly("Size", Spinner(12, 200, halo.Size, v => { halo.Size = v; ApplyInfo(); }, "px"));

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
        rows.AddClassicOnly("Palette", _paletteCombo);

        rows.AddClassicOnly("Low colour", BuildHaloColorPicker(() => halo.LowColor, v => halo.LowColor = v));
        rows.AddClassicOnly("Mid colour", BuildHaloColorPicker(() => halo.MidColor, v => halo.MidColor = v));
        rows.AddClassicOnly("High colour", BuildHaloColorPicker(() => halo.HighColor, v => halo.HighColor = v));
        rows.AddClassicOnly("Charged colour", BuildHaloColorPicker(() => halo.ChargedColor, v => halo.ChargedColor = v));
        rows.AddClassicOnly("Track colour", BuildHaloColorPicker(() => halo.TrackColor, v => halo.TrackColor = v));

        rows.AddRow("Colour by level", Check(halo.ColorByLevel, v => { halo.ColorByLevel = v; ApplyInfo(); }));
        // "up to", not "below": ColorFor tests percent <= threshold, so 20 is still the low band.
        rows.AddRow("Low up to", Spinner(1, 99, halo.LowThreshold, v => { halo.LowThreshold = v; ApplyInfo(); }, "%"));
        rows.AddRow("Mid up to", Spinner(1, 99, halo.MidThreshold, v => { halo.MidThreshold = v; ApplyInfo(); }, "%"));

        rows.AddPlacement("Detach from text", Check(halo.Detached, v =>
        {
            halo.Detached = v;
            UpdateHaloPlacementControls();
            ApplyInfo();
        }));

        var placement = Choice(["Left", "Right", "Above", "Below"], (int)halo.Placement,
            i => { halo.Placement = (HaloPlacement)i; ApplyInfo(); });
        _haloAttachedOnly.Add(placement);
        rows.AddClassicOnly("Beside text", placement);

        var anchor = BuildAnchorPicker(() => halo.Anchor, v => halo.Anchor = v, ApplyInfo);
        var marginX = Spinner(0, 2000, halo.MarginX, v => { halo.MarginX = v; ApplyInfo(); }, "px");
        var marginY = Spinner(0, 2000, halo.MarginY, v => { halo.MarginY = v; ApplyInfo(); }, "px");
        _haloDetachedOnly.AddRange([anchor, marginX, marginY]);
        rows.AddPlacement("Position", anchor);
        rows.AddPlacement("Margin X", marginX);
        rows.AddPlacement("Margin Y", marginY);

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
            // Started from the value being replaced, alpha included. The track colour is
            // deliberately translucent, and the picker opens on the colour as it is — so picking
            // a new hue cannot quietly make it opaque, which is what the old dialog did.
            if (PickColor(existing, allowAlpha: true) is not { } picked) return;
            set(ColorHex.ToHex(picked));
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

    // ---- now-playing record --------------------------------------------------------------

    /// <summary>The record's page. Rotate is the only switch in PrismOS that decides whether a
    /// timer exists at all, so it says so on the label rather than hiding behind "animate".</summary>
    private Control BuildDiscSection()
    {
        var (card, rows) = NewSection("Now playing");
        var disc = _config.Info.Disc;

        rows.AddRow("Show record", Check(disc.Enabled, v => { disc.Enabled = v; ApplyInfo(); }));
        rows.AddClassicOnly("Size", Spinner(40, 400, disc.Size, v => { disc.Size = v; ApplyInfo(); }, "px"));
        rows.AddRow("Spin while playing", Check(disc.Rotate, v => { disc.Rotate = v; ApplyInfo(); }));
        rows.AddRow("Show progress ring", Check(disc.ShowProgress, v => { disc.ShowProgress = v; ApplyInfo(); }));
        rows.AddClassicOnly("Accent colour", BuildHaloColorPicker(() => disc.AccentColor, v => disc.AccentColor = v));

        rows.AddClassicOnly("Title size", Spinner(8, 96, (int)disc.TitleFontSize,
            v => { disc.TitleFontSize = v; ApplyInfo(); }, "px"));
        rows.AddClassicOnly("Artist size", Spinner(6, 72, (int)disc.ArtistFontSize,
            v => { disc.ArtistFontSize = v; ApplyInfo(); }, "px"));
        rows.AddClassicOnly("Artist in capitals", Check(disc.ArtistUppercase,
            v => { disc.ArtistUppercase = v; ApplyInfo(); }));
        rows.AddClassicOnly("Artist spacing", Spinner(0, 12, (int)Math.Round(disc.ArtistLetterSpacing),
            v => { disc.ArtistLetterSpacing = v; ApplyInfo(); }, "px"));
        rows.AddClassicOnly("Artist opacity", Spinner(10, 100, (int)Math.Round(disc.ArtistOpacity * 100),
            v => { disc.ArtistOpacity = v / 100f; ApplyInfo(); }, "%"));

        rows.AddPlacement("Position", BuildAnchorPicker(() => disc.Anchor, v => disc.Anchor = v, ApplyInfo));
        rows.AddPlacement("Margin X", Spinner(0, 2000, disc.MarginX, v => { disc.MarginX = v; ApplyInfo(); }, "px"));
        rows.AddPlacement("Margin Y", Spinner(0, 2000, disc.MarginY, v => { disc.MarginY = v; ApplyInfo(); }, "px"));

        return card;
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
            if (PickColor(InfoColor(), allowAlpha: true) is not { } picked) return;
            SetInfoColor(picked, picked.A);
            button.BackColor = Color.FromArgb(picked.R, picked.G, picked.B);
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

    /// <summary>The wallpaper page: what is on the screen, and how to change it.
    ///
    /// Until now the only way to set a wallpaper was the tray's "Set wallpaper…" item, which
    /// opened a file dialog and nothing else — no way to see what was there, no way to pick a
    /// display, and no way to reach the gallery without the tray. This page does all three: it
    /// shows the wallpaper as it is on the chosen display, applies a new one to that display
    /// alone or to all of them, and opens the gallery as a list rather than a submenu.</summary>
    private Control BuildWallpaperSection()
    {
        var (card, rows) = NewSection("Wallpaper");

        _wallpaperPreview = new WallpaperPreview
        {
            Width = ContentWidth - 32,
            Height = 249,
            Margin = new Padding(0, 0, 0, 12),
        };
        card.Controls.Add(_wallpaperPreview);
        card.Controls.SetChildIndex(_wallpaperPreview, 1);

        rows.AddRow("Display", BuildWallpaperTargetChoice());

        var actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Colors.Card,
            Margin = new Padding(0, 2, 0, 2),
        };
        var browse = new Button
        {
            Text = "Browse…",
            Width = 96,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Colors.Control,
            ForeColor = Theme.Colors.Text,
            Margin = new Padding(0, 0, 8, 0),
        };
        browse.FlatAppearance.BorderColor = Theme.Colors.Border;
        browse.Click += (_, _) => BrowseForWallpaper();

        var gallery = new Button
        {
            Text = "Gallery…",
            Width = 96,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Colors.Control,
            ForeColor = Theme.Colors.Text,
        };
        gallery.FlatAppearance.BorderColor = Theme.Colors.Border;
        gallery.Click += (_, _) => OpenGallery();

        actions.Controls.Add(browse);
        actions.Controls.Add(gallery);
        rows.AddRow("Change wallpaper", actions);

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

        _wallpaperCreditHost = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Colors.Card,
            Margin = new Padding(0),
        };
        _wallpaperCreditCaption = rows.AddRowLabel("Credit", _wallpaperCreditHost);

        RefreshWallpaperSection();
        return card;
    }

    /// <summary>Which display the page is about, and where a newly chosen wallpaper goes.
    ///
    /// One list, doing both jobs, rather than a "show display" and an "apply to" that could
    /// disagree: the thing you are looking at is the thing you are changing.</summary>
    private Control BuildWallpaperTargetChoice()
    {
        var targets = WallpaperTargets.Build(Desktop.MonitorTracker.Enumerate());
        _wallpaperTargets.Clear();
        _wallpaperTargets.AddRange(targets.Select(t => t.Device));

        return Choice([.. targets.Select(t => t.Label)], 0, i =>
        {
            if (_loading) return;
            _wallpaperTargetIndex = i;
            RefreshWallpaperSection();
        });
    }

    /// <summary>The device the page is about: "*" for every display, or a monitor's device name.</summary>
    private string CurrentWallpaperTarget() =>
        _wallpaperTargetIndex >= 0 && _wallpaperTargetIndex < _wallpaperTargets.Count
            ? _wallpaperTargets[_wallpaperTargetIndex]
            : "*";

    private string CurrentWallpaperTargetLabel() => WallpaperTargets.Describe(CurrentWallpaperTarget());

    /// <summary>Redraws the preview and the credit for the selected display.</summary>
    private void RefreshWallpaperSection()
    {
        if (_wallpaperPreview is null) return;
        string device = CurrentWallpaperTarget();
        var path = _engine.Config.WallpaperFor(device);
        bool exists = path is not null && File.Exists(path);

        // A video plays live in the panel only with the live preview enabled; otherwise it
        // shows the captured still, the same frame the Style tiles use.
        string? previewSource = path;
        bool liveVideo = exists && _config.LiveVideoPreview && WallpaperPreview.IsVideoPath(path!);
        if (exists && !liveVideo && WallpaperPreview.IsVideoPath(path!))
            previewSource = _engine.WallpaperStillPath(device);
        _wallpaperPreview.SetSource(previewSource);
        _wallpaperPreview.Caption = path is not null && exists ? Path.GetFileName(path) : "";
        _wallpaperPreview.Placeholder = path is null ? "No wallpaper on this display"
            : !exists ? $"Not found: {Path.GetFileName(path)}"
            : !liveVideo && WallpaperPreview.IsVideoPath(path!) ? "Live preview is off — no still frame captured yet. Turn on Live video preview in Behaviour."
            : "Loading preview…";

        var credit = _engine.CurrentWallpaperCreditFor(device);
        if (_wallpaperCreditHost is not null)
        {
            ControlTree.ClearAndDispose(_wallpaperCreditHost);
            if (credit is not null) _wallpaperCreditHost.Controls.Add(BuildCredit(credit));
        }
        if (_wallpaperCreditCaption is not null) _wallpaperCreditCaption.Visible = credit is not null;
    }

    private void BrowseForWallpaper()
    {
        if (FilePicker.PickMedia(Handle) is not { } path) return;
        _engine.ApplyUserSelection(path, CurrentWallpaperTarget());
        RefreshWallpaperSection();
    }

    private void OpenGallery()
    {
        using var form = new GalleryForm(_engine, CurrentWallpaperTarget(), CurrentWallpaperTargetLabel());
        form.ShowDialog(this);
        RefreshWallpaperSection();
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
        if (_batteryAvailable)
            rows.AddRow("Pause on battery saver", Check(pause.OnBatterySaver, v => { pause.OnBatterySaver = v; _engine.SaveConfig(); }));
        rows.AddRow("Pause in remote desktop", Check(pause.OnRemoteSession, v => { pause.OnRemoteSession = v; _engine.SaveConfig(); }));
        rows.AddRow("Live video preview", Check(_config.LiveVideoPreview, v => { _config.LiveVideoPreview = v; _engine.SaveConfig(); RefreshWallpaperSection(); }));
        rows.AddRow("Blur background (frosted glass)", Check(_config.Widgets.Blur, v => { _config.Widgets.Blur = v; _engine.SaveConfig(); _engine.ReapplyWallpaperAndWidgets(); }));
        rows.AddRow("Show on lock screen", Check(_config.Widgets.LockScreen, v => { _config.Widgets.LockScreen = v; _engine.SaveConfig(); if (v) _engine.ApplyLockScreenWallpaper(); }));
        rows.AddRow("Wake-up transition", Check(_config.Widgets.WakeTransition, v => { _config.Widgets.WakeTransition = v; _engine.SaveConfig(); }));
        rows.AddRow("Start with Windows", Check(Autostart.IsEnabled(), Autostart.SetEnabled));

        var reset = new Button
        {
            Text = "Reset to defaults",
            Width = 140,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Colors.Control,
            ForeColor = Theme.Colors.Text,
        };
        reset.FlatAppearance.BorderColor = Theme.Colors.Border;
        reset.Click += (_, _) => ConfirmAndReset();
        rows.AddRow("Reset", reset);

        return card;
    }

    /// <summary>Asks before resetting, because the answer cannot be undone.
    ///
    /// The wording says what survives, not just what goes: a person who has set a wallpaper they
    /// like should not have to wonder whether the button takes it with them.</summary>
    private void ConfirmAndReset()
    {
        const string message =
            "Reset every setting to how PrismOS ships?\n\n" +
            "The clock, the date, the widgets, the colours and the behaviour all go back to their " +
            "defaults, and the widget styles return to Classic. Your wallpapers are kept, and so is " +
            "the wallpaper PrismOS will put back when it quits.\n\n" +
            "This cannot be undone.";

        if (User32.MessageBoxW(Handle, message, "Reset PrismOS", 0x24 /* MB_ICONQUESTION | MB_YESNO */) != 6 /* IDYES */)
            return;

        _engine.ResetSettingsToDefaults();
        RebuildPages();
    }

    /// <summary>The About page: what this is, where it came from, and whether there is a newer
    /// release.
    ///
    /// The attribution is on the page rather than only in a readme. A fork that hides where it
    /// came from is a fork that has taken someone's work, and the person who wrote the original
    /// should be named on the app itself, not just in a file nobody opens.</summary>
    private Control BuildAboutSection()
    {
        var (card, rows) = NewSection("About PrismOS");

        var version = UpdateCheck.CurrentVersion();
        rows.AddRow("Version", TextValue($"{version} · .NET {Environment.Version.Major}.{Environment.Version.Minor}"));

        rows.AddRow("Project", Link("YUouriii/PrismOS", UpdateCheck.RepositoryUrl));

        rows.AddRow("Based on", Link("FeatherWall by Ritvik Dayal", UpdateCheck.UpstreamUrl));

        rows.AddRow("Licence", TextValue("MIT"));

        var status = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = Theme.Colors.Subtle,
            Margin = new Padding(8, 6, 0, 0),
        };
        var check = new Button
        {
            Text = "Check for updates",
            Width = 140,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Colors.Control,
            ForeColor = Theme.Colors.Text,
        };
        check.FlatAppearance.BorderColor = Theme.Colors.Border;
        check.Click += async (_, _) => await CheckForUpdatesAsync(check, status);

        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Colors.Card,
            Margin = new Padding(0, 2, 0, 2),
        };
        row.Controls.Add(check);
        row.Controls.Add(status);
        rows.AddRow("Updates", row);

        // The two things that used to live behind right-click live here, with the rest of the
        // management this window exists for.
        var aux = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Colors.Card,
            Margin = new Padding(0, 2, 0, 2),
        };
        var openConfig = new Button
        {
            Text = "Open config file",
            AutoSize = true,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Colors.Control,
            ForeColor = Theme.Colors.Text,
        };
        openConfig.FlatAppearance.BorderColor = Theme.Colors.Border;
        openConfig.Click += (_, _) =>
        {
            try
            {
                _engine.SaveConfig();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ConfigStore.ConfigPath) { UseShellExecute = true });
            }
            catch (Exception ex) { Common.Log.Warn($"Could not open the config file: {ex.Message}"); }
        };
        var diagnostics = new Button
        {
            Text = "Diagnostics",
            AutoSize = true,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Colors.Control,
            ForeColor = Theme.Colors.Text,
        };
        diagnostics.FlatAppearance.BorderColor = Theme.Colors.Border;
        diagnostics.Click += (_, _) =>
            User32.MessageBoxW(Handle, _engine.Diagnostics(), "PrismOS diagnostics", 0x40 /* MB_ICONINFORMATION */);
        aux.Controls.Add(openConfig);
        aux.Controls.Add(diagnostics);
        rows.AddRow("Files", aux);

        return card;
    }

    /// <summary>Asks GitHub for the latest release. A check that could not be made says so:
    /// "up to date" would be a claim the app has no way to support.</summary>
    private async Task CheckForUpdatesAsync(Button check, Label status)
    {
        check.Enabled = false;
        status.ForeColor = Theme.Colors.Subtle;
        status.Text = "Checking…";
        try
        {
            var result = await UpdateCheck.CheckAsync();
            switch (result.Outcome)
            {
                case UpdateCheckOutcome.UpToDate:
                    status.Text = "You are up to date";
                    return;
                case UpdateCheckOutcome.NoReleases:
                    status.Text = "No release published yet";
                    return;
                case UpdateCheckOutcome.Newer:
                    status.Text = $"{result.Tag} is available";
                    status.ForeColor = Theme.Colors.Accent;
                    status.Cursor = Cursors.Hand;
                    status.Click += (_, _) => OpenUrl(result.Url);
                    return;
                default:
                    status.Text = "Could not check";
                    return;
            }
        }
        catch (Exception ex)
        {
            Common.Log.Warn($"Update check failed: {ex.Message}");
            status.Text = "Could not check";
        }
        finally
        {
            check.Enabled = true;
        }
    }

    /// <summary>A read-only value in the value column.</summary>
    private Control TextValue(string value) => new Label
    {
        Text = value,
        AutoSize = true,
        ForeColor = Theme.Colors.Text,
        Margin = new Padding(0, 5, 0, 5),
    };

    /// <summary>A link that opens in the browser.</summary>
    private LinkLabel Link(string text, string url)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            LinkColor = Theme.Colors.Accent,
            ActiveLinkColor = Theme.Colors.Accent,
            Margin = new Padding(0, 5, 0, 5),
        };
        link.LinkClicked += (_, _) => OpenUrl(url);
        return link;
    }

    private void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) { Common.Log.Warn($"Could not open {url}: {ex.Message}"); }
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
            if (PickColor(CurrentColor(), allowAlpha: true) is not { } picked) return;
            SetColor(picked, picked.A);
            _colorButton.BackColor = Color.FromArgb(picked.R, picked.G, picked.B);
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
        private readonly SettingsForm _owner;

        public RowGrid(SettingsForm owner)
        {
            _owner = owner;
            ColumnCount = 2;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Theme.Colors.Card;
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }

        public void AddRow(string label, Control control) => Add(label, control);

        public Label AddRowLabel(string label, Control control) => Add(label, control);

        /// <summary>A setting only the Classic theme reads. It stays editable under another
        /// theme — it is Classic's value for when Classic is chosen again — but says so.</summary>
        public void AddClassicOnly(string label, Control control) =>
            _owner._classicOnly.Add((Add(label, control), label));

        /// <summary>A per-widget position: unused while a group theme places the set.</summary>
        public void AddPlacement(string label, Control control) =>
            _owner._placement.Add((Add(label, control), label));

        /// <summary>A row that only applies to the group themes; hidden otherwise.</summary>
        public void AddGroupOnly(string label, Control control)
        {
            _owner._groupRows.Add(Add(label, control));
            _owner._groupRows.Add(control);
        }

        private Label Add(string label, Control control)
        {
            var caption = new Label
            {
                Text = label,
                AutoSize = true,
                MaximumSize = new Size(LabelColumnWidth - 12, 0),
                ForeColor = Theme.Colors.Text,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 12, 6),
            };
            control.Anchor = AnchorStyles.Left;
            Controls.Add(caption);
            Controls.Add(control);
            return caption;
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

    /// <summary>The colour picker, or null when it was cancelled.
    ///
    /// One dialog for every colour in the app, so the accent, the clock, the date, the info
    /// widget and the halo all pick the same way. The Windows colour dialog it replaces could
    /// not express alpha at all, which is why each of those colours had an opacity spinner
    /// beside it: the two halves of one colour lived in two places.</summary>
    private Color? PickColor(Color current, bool allowAlpha)
    {
        using var picker = new ColorPickerForm(current, allowAlpha);
        return picker.ShowDialog(this) == DialogResult.OK ? picker.SelectedColor : null;
    }

    /// <summary>The clock's colour as it is drawn, alpha included.</summary>
    private Color CurrentColor() => Widgets.ClockRenderer.ParseColor(_config.Clock.Color);

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
