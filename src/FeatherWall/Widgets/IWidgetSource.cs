namespace FeatherWall.Widgets;

/// <summary>A value a widget can display, and a signal when it changes.
///
/// Null Value means there is nothing to show and the line disappears — a desktop has no
/// battery, and most of the time nothing is playing. Rendering "N/A" would fill the wallpaper
/// with the absence of information.
///
/// Sources push; nothing here polls. Changed is raised only when the text actually moves, so a
/// battery reading the same percentage twice costs one comparison and no repaint.</summary>
public interface IWidgetSource : IDisposable
{
    string? Value { get; }

    event Action? Changed;
}
