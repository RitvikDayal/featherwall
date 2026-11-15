using PrismOS.Desktop;

namespace PrismOS.Tray;

/// <summary>One choice on the Wallpaper page's display list: what it says, and which display it
/// points at. "*" means every display.</summary>
public sealed record WallpaperTarget(string Label, string Device);

/// <summary>The displays a wallpaper can be applied to.
///
/// Kept apart from the settings form so the list can be asserted without a window: the mapping
/// from a display to its label is the part that has to be right, and a label that lies about
/// which display it is would put a wallpaper on the wrong screen.</summary>
public static class WallpaperTargets
{
    public static List<WallpaperTarget> Build(IReadOnlyList<MonitorInfo> monitors)
    {
        var targets = new List<WallpaperTarget> { new("All displays", "*") };
        foreach (var monitor in monitors)
        {
            string name = monitor.Device.Replace(@"\\.\", "");
            string size = $"{monitor.Bounds.Width}×{monitor.Bounds.Height}";
            targets.Add(new(monitor.Primary
                ? $"Primary display — {name} ({size})"
                : $"{name} — {size}",
                monitor.Device));
        }
        return targets;
    }

    /// <summary>The label for the display a device name points at, for messages that name it.</summary>
    public static string Describe(string device) =>
        device == "*" ? "all displays" : device.Replace(@"\\.\", "");
}
