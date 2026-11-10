using System.Drawing;
using FeatherWall.Config;

namespace FeatherWall.Widgets.Themes;

/// <summary>What an overlay needs to know to draw in a theme: which one, and the accent for the
/// monitor it sits on.</summary>
public sealed record WidgetStyle(WidgetTheme Theme, Color Accent)
{
    public static WidgetStyle Classic { get; } = new(WidgetTheme.Classic, Themes.Accent.Fallback);
}
