using PrismOS.Gallery;

namespace PrismOS.Tray;

/// <summary>The built-in gallery as a list: every public-domain / CC0 entry, what it is, how big
/// it is, and a button that downloads it and puts it on the chosen display.
///
/// The tray has had a gallery submenu since the start, but a submenu is a list of names: it cannot
/// say what a wallpaper is, how large the download is, or that it is already on disk. This can,
/// and it is where a person looks when they want to change the wallpaper rather than a setting.</summary>
public sealed class GalleryForm : Form
{
    private const int RowHeight = 100;
    private const int RowGap = 8;

    private readonly Engine _engine;
    private readonly string _device;
    private readonly List<GalleryRow> _rows = [];
    private readonly Panel _scroll;
    private readonly FlowLayoutPanel _footer;
    private readonly Label _header;

    public GalleryForm(Engine engine, string device, string displayLabel)
    {
        _engine = engine;
        _device = device;

        Text = "Gallery";
        Icon = PrismIcon.CreateIcon();
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        ShowInTaskbar = false;
        BackColor = Theme.Colors.Window;
        ForeColor = Theme.Colors.Text;

        _header = new Label
        {
            Text = $"Public-domain and CC0 wallpapers. Nothing is uploaded, and a wallpaper is only " +
                   $"downloaded when you ask for it.\nApplying to: {displayLabel}",
            Dock = DockStyle.Top,
            Height = 52,
            ForeColor = Theme.Colors.Subtle,
            Padding = new Padding(16, 12, 16, 0),
            BackColor = Theme.Colors.Window,
        };

        _scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Colors.Window,
            Padding = new Padding(16, 0, 8, 12),
        };

        foreach (var entry in engine.GalleryEntries)
        {
            var row = new GalleryRow(entry, engine.IsGalleryDownloaded(entry));
            row.Use += async () => await UseAsync(row, entry);
            _rows.Add(row);
            _scroll.Controls.Add(row);
        }

        _footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 56,
            Padding = new Padding(16, 12, 16, 12),
            BackColor = Theme.Colors.Window,
        };
        var close = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.Cancel,
            Width = 100,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Colors.Control,
            ForeColor = Theme.Colors.Text,
        };
        close.FlatAppearance.BorderColor = Theme.Colors.Border;
        _footer.Controls.Add(close);
        CancelButton = close;

        // Order matters: WinForms docks the last control added first, so the footer takes the
        // bottom, the header the top, and the scrolling list what is left.
        Controls.Add(_scroll);
        Controls.Add(_header);
        Controls.Add(_footer);

        Resize += (_, _) => LayoutRows();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ClientSize = new Size(640, 620);
        LayoutRows();
    }

    /// <summary>Positions the rows in a single column and tells the scroll panel how tall the
    /// content is. Rows are placed by hand rather than by a flow layout: a flow layout that wraps
    /// would put two entries side by side in a narrow window and cut their text in half.</summary>
    private void LayoutRows()
    {
        int width = Math.Max(320, _scroll.ClientSize.Width - _scroll.Padding.Horizontal - 24);
        int y = 0;
        foreach (var row in _rows)
        {
            row.SetBounds(16, y, width, RowHeight);
            row.LayoutRow(width);
            y += RowHeight + RowGap;
        }
        _scroll.AutoScrollMinSize = new Size(width + 24, y + 12);
    }

    private async Task UseAsync(GalleryRow row, GalleryEntry entry)
    {
        if (row.Busy) return;
        row.Busy = true;
        try
        {
            var progress = new Progress<double>(p => row.SetStatus($"{p * 100:0}%"));
            string path = await _engine.DownloadGalleryEntryAsync(entry, progress);
            _engine.ApplyUserSelection(path, _device);
            row.SetStatus("Applied");
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Common.Log.Error($"Gallery download failed: {entry.Id}", ex);
            row.SetStatus($"Failed: {ex.Message}");
            row.Busy = false;
        }
    }
}

/// <summary>One gallery entry: what it is, and the button that fetches it.</summary>
internal sealed class GalleryRow : Panel
{
    private readonly Label _title;
    private readonly Label _meta;
    private readonly Label _description;
    private readonly Label _status;
    private readonly Button _use;
    private readonly LinkLabel _source;

    public event Func<Task>? Use;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Busy { get; set; }

    public GalleryRow(GalleryEntry entry, bool downloaded)
    {
        BackColor = Theme.Colors.Card;
        Margin = new Padding(0);

        _title = new Label
        {
            Text = entry.Title,
            AutoSize = false,
            ForeColor = Theme.Colors.Text,
            Font = new Font("Segoe UI Semibold", 10f),
            BackColor = Theme.Colors.Card,
        };

        var kind = entry.Type == "video" ? "video" : "image";
        var resolution = entry.Width > 0 ? $"{entry.Width}×{entry.Height}" : "unknown size";
        _meta = new Label
        {
            Text = $"{kind} · {resolution} · {entry.SizeLabel}{(downloaded ? " · already downloaded" : "")}",
            AutoSize = false,
            ForeColor = Theme.Colors.Subtle,
            BackColor = Theme.Colors.Card,
        };

        _description = new Label
        {
            Text = entry.Description,
            AutoSize = false,
            ForeColor = Theme.Colors.Subtle,
            BackColor = Theme.Colors.Card,
        };

        _status = new Label
        {
            Text = "",
            AutoSize = false,
            ForeColor = Theme.Colors.Subtle,
            BackColor = Theme.Colors.Card,
        };

        _use = new Button
        {
            Text = downloaded ? "Use" : "Download",
            Width = 100,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Colors.Accent,
            ForeColor = Color.White,
        };
        _use.FlatAppearance.BorderSize = 0;
        _use.Click += async (_, _) =>
        {
            if (Use is null) return;
            try { await Use(); }
            catch (Exception ex) { SetStatus($"Failed: {ex.Message}"); }
        };

        _source = new LinkLabel
        {
            Text = "Source",
            AutoSize = true,
            LinkColor = Theme.Colors.Accent,
            ActiveLinkColor = Theme.Colors.Accent,
            BackColor = Theme.Colors.Card,
        };
        _source.LinkClicked += (_, _) =>
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

        Controls.AddRange([_title, _meta, _description, _status, _use, _source]);
    }

    public void SetStatus(string text)
    {
        _status.Text = text;
        _status.Visible = text.Length > 0;
    }

    /// <summary>Positions the row's parts for a given width. The text blocks take what is left
    /// after the button column, so they never run under the button.</summary>
    public void LayoutRow(int width)
    {
        const int buttonWidth = 100;
        const int gap = 16;
        int textWidth = Math.Max(120, width - buttonWidth - gap * 2);

        _title.SetBounds(gap, 10, textWidth, 22);
        _meta.SetBounds(gap, 32, textWidth, 18);
        _description.SetBounds(gap, 51, textWidth, 30);
        _status.SetBounds(gap, 80, textWidth, 18);
        _use.SetBounds(width - buttonWidth - gap, 14, buttonWidth, 30);
        _source.SetBounds(width - buttonWidth - gap, 52, buttonWidth, 18);
    }
}
