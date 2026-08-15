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
}

public sealed class PauseConfig
{
    public bool OnFullscreen { get; set; } = true;
    public bool OnBatterySaver { get; set; } = true;
    public bool OnRemoteSession { get; set; } = true;
}

public sealed class AppConfig
{
    public List<WallpaperAssignment> Wallpapers { get; set; } = [];
    public FitMode Fit { get; set; } = FitMode.Fill;
    public bool MuteVideo { get; set; } = true;
    public double Volume { get; set; } = 0.3;
    public ClockConfig Clock { get; set; } = new();
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
