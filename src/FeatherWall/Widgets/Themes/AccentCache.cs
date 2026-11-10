using System.Drawing;
using FeatherWall.Common;

namespace FeatherWall.Widgets.Themes;

/// <summary>Each monitor's sampled accent, keyed by wallpaper path so the same wallpaper is never
/// sampled twice.
///
/// Every monitor's latest static frame is remembered whatever the theme, but sampled only when a
/// theme asks for the colour. Classic never asks, so a Classic desktop never reads the PNG.</summary>
public sealed class AccentCache(Func<string, Color?> sampleFile)
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, (string Wallpaper, string Png)> _frames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Color?> _byWallpaper = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A monitor's static frame was written. True when the theme uses an accent and this
    /// monitor's colour is now different — the caller's cue to rebuild the widgets, once.</summary>
    public bool OnStaticFrame(string monitorDevice, string wallpaperPath, string staticPngPath, bool themeUsesAccent)
    {
        lock (_sync)
        {
            var before = _frames.TryGetValue(monitorDevice, out var old) && _byWallpaper.TryGetValue(old.Wallpaper, out var c)
                ? c
                : null;
            _frames[monitorDevice] = (wallpaperPath, staticPngPath);
            if (!themeUsesAccent) return false;
            return SampleLocked(wallpaperPath, staticPngPath) != before;
        }
    }

    /// <summary>Null before any frame, or when the wallpaper has no colour worth taking.</summary>
    public Color? For(string monitorDevice)
    {
        lock (_sync)
            return _frames.TryGetValue(monitorDevice, out var frame) ? SampleLocked(frame.Wallpaper, frame.Png) : null;
    }

    private Color? SampleLocked(string wallpaper, string png)
    {
        if (_byWallpaper.TryGetValue(wallpaper, out var cached)) return cached;

        Color? colour = null;
        try
        {
            colour = sampleFile(png);
            Log.Info($"Accent sampled for {wallpaper}: {(colour is { } c ? ColorTranslator.ToHtml(c) : "none, using the fallback")}");
        }
        catch (Exception ex)
        {
            // Cached as null too: a frame that cannot be read once will not read on the next ask.
            Log.Warn($"Accent sample failed for {wallpaper}: {ex.Message}");
        }
        _byWallpaper[wallpaper] = colour;
        return colour;
    }
}
