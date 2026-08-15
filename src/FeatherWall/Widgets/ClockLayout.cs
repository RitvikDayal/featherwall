using FeatherWall.Config;
using FeatherWall.Interop;

namespace FeatherWall.Widgets;

public static class ClockLayout
{
    /// <summary>Top-left position (virtual-screen coords) for a widget of the given size
    /// anchored inside <paramref name="area"/> (a monitor's work area) with margins.</summary>
    public static POINT Position(in RECT area, int widgetW, int widgetH, ClockAnchor anchor, int marginX, int marginY)
    {
        int x = anchor switch
        {
            ClockAnchor.TopLeft or ClockAnchor.CenterLeft or ClockAnchor.BottomLeft => area.Left + marginX,
            ClockAnchor.TopCenter or ClockAnchor.Center or ClockAnchor.BottomCenter => area.Left + (area.Width - widgetW) / 2,
            _ => area.Right - widgetW - marginX,
        };
        int y = anchor switch
        {
            ClockAnchor.TopLeft or ClockAnchor.TopCenter or ClockAnchor.TopRight => area.Top + marginY,
            ClockAnchor.CenterLeft or ClockAnchor.Center or ClockAnchor.CenterRight => area.Top + (area.Height - widgetH) / 2,
            _ => area.Bottom - widgetH - marginY,
        };
        return new POINT { X = x, Y = y };
    }

    public static string TimeText(DateTime now, bool twentyFourHour, bool showSeconds)
    {
        string format = (twentyFourHour, showSeconds) switch
        {
            (true, true) => "HH:mm:ss",
            (true, false) => "HH:mm",
            (false, true) => "h:mm:ss tt",
            (false, false) => "h:mm tt",
        };
        return now.ToString(format, System.Globalization.CultureInfo.CurrentCulture);
    }

    public static string DateText(DateTime now) =>
        now.ToString("dddd, MMMM d", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Milliseconds until the next tick boundary (second or minute), re-armed every
    /// tick so the clock never drifts off the boundary.</summary>
    public static int MillisecondsToNextTick(DateTime now, bool showSeconds)
    {
        int ms = showSeconds
            ? 1000 - now.Millisecond
            : (59 - now.Second) * 1000 + (1000 - now.Millisecond);
        return Math.Max(ms, 15);
    }
}
