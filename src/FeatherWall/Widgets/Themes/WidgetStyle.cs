using System.Drawing;
using FeatherWall.Config;

namespace FeatherWall.Widgets.Themes;

/// <summary>What an overlay needs to know to draw in a theme: which one, and the accent for the
/// monitor it sits on. <paramref name="Size"/> is the theme's overall scale before the monitor's
/// DPI factor, which each overlay applies itself.</summary>
public sealed record WidgetStyle(WidgetTheme Theme, Color Accent, float Size = 1f)
{
    public static WidgetStyle Classic { get; } = new(WidgetTheme.Classic, Themes.Accent.Fallback);
}

public static class WidgetStyles
{
    /// <summary>A new accent is worth a rebuild only if a widget sits on that monitor and the
    /// style it would now get differs from the one it was built with. Startup delivers a static
    /// frame per monitor; without this, a monitor carrying no widget rebuilt them all.</summary>
    public static bool ShouldRebuild(IReadOnlyDictionary<string, WidgetStyle> inUse, string monitorDevice,
                                     WidgetStyle now) =>
        inUse.TryGetValue(monitorDevice, out var used) && used != now;
}
