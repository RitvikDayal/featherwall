using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeatherWall.Config;

public enum FitMode { Fill, Fit, Stretch }

public enum ClockAnchor
{
    TopLeft, TopCenter, TopRight,
    CenterLeft, Center, CenterRight,
    BottomLeft, BottomCenter, BottomRight,
}

public sealed class WallpaperAssignment
{
    /// <summary>Monitor device name (e.g. \\.\DISPLAY1); "*" applies to all monitors.</summary>
    public string Monitor { get; set; } = "*";
    public string Path { get; set; } = "";
}

public sealed class ClockConfig
{
    public bool Enabled { get; set; } = true;
    public string Monitor { get; set; } = "*";
    public ClockAnchor Anchor { get; set; } = ClockAnchor.TopCenter;
    public int MarginX { get; set; } = 48;
    public int MarginY { get; set; } = 96;

    /// <summary>Per-edge overrides. Null inherits MarginX (left/right) or MarginY (top/bottom),
    /// so an untouched config behaves exactly as it did before these existed. Only the edges the
    /// anchor actually touches are consulted.</summary>
    public int? MarginLeft { get; set; }
    public int? MarginRight { get; set; }
    public int? MarginTop { get; set; }
    public int? MarginBottom { get; set; }
    public bool TwentyFourHour { get; set; } = false;
    public bool ShowSeconds { get; set; } = false;
    public bool ShowDate { get; set; } = true;
    /// <summary>Thin horizontal rule between time and date (Mond style).</summary>
    public bool Separator { get; set; } = true;
    public float FontSize { get; set; } = 110f;
    /// <summary>Use a light-weight face for the big time ("Segoe UI Light").</summary>
    public string FontFamily { get; set; } = "Segoe UI Light";
    /// <summary>#RRGGBB or #AARRGGBB.</summary>
    public string Color { get; set; } = "#F0FFFFFF";
    public bool Shadow { get; set; } = true;

    // ---- date styling ---------------------------------------------------------------------
    // Requested on r/coolgithubprojects by a user two days into daily use: the date could not be
    // styled at all, because CreateDateFont hardcoded "Segoe UI" at 16% of the time's size.
    // Every default below reproduces that exact rendering, so upgrading changes nothing until
    // someone actually sets one.

    /// <summary>Face for the date line. Null or empty inherits the time's <see cref="FontFamily"/>.</summary>
    public string? DateFontFamily { get; set; } = "Segoe UI";

    /// <summary>Date size as a fraction of the time's font size.</summary>
    public float DateFontScale { get; set; } = 0.16f;

    /// <summary>Floor in pixels, so the date stays legible under a small clock.</summary>
    public float DateMinFontSize { get; set; } = 11f;

    /// <summary>#RRGGBB or #AARRGGBB for the date. Null inherits the time's colour, dimmed by
    /// <see cref="DateOpacity"/>.</summary>
    public string? DateColor { get; set; }

    /// <summary>Applied to the inherited colour's alpha. Ignored when DateColor is set.</summary>
    public float DateOpacity { get; set; } = 0.80f;
}

public sealed class PauseConfig
{
    public bool OnFullscreen { get; set; } = true;
    public bool OnBatterySaver { get; set; } = true;
    public bool OnRemoteSession { get; set; } = true;
}

/// <summary>The info widget: a stack of lines fed by system sources, anchored independently of
/// the clock. Disabled by default — an upgrade must render exactly as it did before, the same
/// rule the date-styling defaults follow.</summary>
public sealed class InfoConfig
{
    public bool Enabled { get; set; } = false;
    public string Monitor { get; set; } = "*";
    public ClockAnchor Anchor { get; set; } = ClockAnchor.BottomLeft;
    public int MarginX { get; set; } = 48;
    public int MarginY { get; set; } = 48;

    /// <summary>Per-edge overrides, null inheriting MarginX/MarginY, exactly as ClockConfig does.</summary>
    public int? MarginLeft { get; set; }
    public int? MarginRight { get; set; }
    public int? MarginTop { get; set; }
    public int? MarginBottom { get; set; }

    public float FontSize { get; set; } = 22f;
    public string FontFamily { get; set; } = "Segoe UI";
    public string Color { get; set; } = "#C0FFFFFF";
    public bool Shadow { get; set; } = true;

    /// <summary>Truncation budget. Scrolling a long title would mean animating, and animating
    /// means waking up.</summary>
    public int MaxCharacters { get; set; } = 48;

    /// <summary>Ordered; the order is the display order. An unknown name is logged and skipped
    /// rather than throwing, so a config written by a later version still starts this one.</summary>
    public List<string> Sources { get; set; } = ["nowPlaying", "battery"];

    public HaloConfig Halo { get; set; } = new();
    public DiscConfig Disc { get; set; } = new();
}

/// <summary>The now-playing record: album art on the label, turning while the music plays.
///
/// Rotate is the only thing in FeatherWall that starts a timer. Setting it false gives the whole
/// design — art, progress, the lot — as a still image, and creates no timer at all.</summary>
public sealed class DiscConfig
{
    public bool Enabled { get; set; } = true;
    public int Size { get; set; } = 112;
    public bool Rotate { get; set; } = true;
    public bool ShowProgress { get; set; } = true;

    /// <summary>Its own anchor, independent of the info text and the halo — the record is its own
    /// block, not a line in a list.</summary>
    public ClockAnchor Anchor { get; set; } = ClockAnchor.BottomLeft;
    public int MarginX { get; set; } = 48;
    public int MarginY { get; set; } = 48;
    public int? MarginLeft { get; set; }
    public int? MarginRight { get; set; }
    public int? MarginTop { get; set; }
    public int? MarginBottom { get; set; }

    /// <summary>Used for the progress ring, and for the label when a track has no artwork.</summary>
    public string AccentColor { get; set; } = "#8FB4FF";

    // Typography follows the clock's hierarchy rather than the info widget's uniform text: a
    // near-white title, and a smaller dimmed artist set in spaced capitals underneath. That is
    // the same relationship the clock has between the time and the date.
    public float TitleFontSize { get; set; } = 19f;
    public float ArtistFontSize { get; set; } = 14f;
    public bool ArtistUppercase { get; set; } = true;
    public float ArtistLetterSpacing { get; set; } = 1.4f;
    public float ArtistOpacity { get; set; } = 0.62f;
}

public enum HaloPlacement { Left, Right, Above, Below }

/// <summary>The battery halo: a ring whose arc is the charge level and whose colour steps with it.
///
/// On by default, which bends the rule that an upgrade must never silently add something to
/// someone's wallpaper. Deliberate and narrow: the info widget this attaches to shipped in the
/// same release and has no installed base to surprise, so the population that rule protects is
/// empty. Had the widget shipped a release earlier this would default to false.</summary>
public sealed class HaloConfig
{
    public bool Enabled { get; set; } = true;
    /// <summary>Bigger than it was: the percentage is drawn inside the ring now, and a number in
    /// a 34 px circle is not legible.</summary>
    public int Size { get; set; } = 44;

    /// <summary>False: drawn beside the info lines and moving with them. True: its own overlay,
    /// its own anchor and its own margins, and Placement is ignored.</summary>
    public bool Detached { get; set; } = false;
    public HaloPlacement Placement { get; set; } = HaloPlacement.Left;

    public ClockAnchor Anchor { get; set; } = ClockAnchor.TopRight;
    public int MarginX { get; set; } = 48;
    public int MarginY { get; set; } = 48;
    public int? MarginLeft { get; set; }
    public int? MarginRight { get; set; }
    public int? MarginTop { get; set; }
    public int? MarginBottom { get; set; }

    /// <summary>False uses HighColor at every level, so one fixed colour is a supported choice
    /// rather than something you have to fake by setting all four the same.</summary>
    public bool ColorByLevel { get; set; } = true;

    // Red at the bottom, orange in the middle, green when healthy — the conventional battery
    // reading. Thresholds are inclusive upper bounds. The warm Ember ramp is still available as a
    // preset; it is not the default, because a gold "high" says less than a green one.
    public string LowColor { get; set; } = "#FF4D4D";
    public int LowThreshold { get; set; } = 20;
    public string MidColor { get; set; } = "#FF9A3C";
    public int MidThreshold { get; set; } = 50;
    public string HighColor { get; set; } = "#5FD98A";
    public string ChargedColor { get; set; } = "#7CE8A4";
    public string TrackColor { get; set; } = "#24FFFFFF";
}

public sealed class AppConfig
{
    public List<WallpaperAssignment> Wallpapers { get; set; } = [];
    public FitMode Fit { get; set; } = FitMode.Fill;
    public bool MuteVideo { get; set; } = true;
    public double Volume { get; set; } = 0.3;
    public ClockConfig Clock { get; set; } = new();
    public InfoConfig Info { get; set; } = new();
    public PauseConfig Pause { get; set; } = new();

    public string? WallpaperFor(string monitorDevice)
    {
        var exact = Wallpapers.FirstOrDefault(w => string.Equals(w.Monitor, monitorDevice, StringComparison.OrdinalIgnoreCase));
        var star = Wallpapers.FirstOrDefault(w => w.Monitor == "*");
        var path = (exact ?? star)?.Path;
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public void Assign(string monitorDevice, string path)
    {
        var existing = Wallpapers.FirstOrDefault(w => string.Equals(w.Monitor, monitorDevice, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) existing.Path = path;
        else Wallpapers.Add(new WallpaperAssignment { Monitor = monitorDevice, Path = path });
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(AppConfig))]
public sealed partial class ConfigJsonContext : JsonSerializerContext;

public static class ConfigStore
{
    public static string ConfigDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FeatherWall");

    public static string ConfigPath { get; } = Path.Combine(ConfigDirectory, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize(File.ReadAllText(ConfigPath), ConfigJsonContext.Default.AppConfig) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            Common.Log.Warn($"Config unreadable, using defaults ({ex.Message})");
        }
        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, ConfigJsonContext.Default.AppConfig));
        File.Move(tmp, ConfigPath, overwrite: true);
    }
}
