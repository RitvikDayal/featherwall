using FeatherWall.Interop;

namespace FeatherWall.Playback;

public sealed record ForegroundInfo(string ClassName, RECT WindowRect, bool IsZoomed, IntPtr MonitorHandle);

/// <summary><paramref name="DisplayOff"/> comes from GUID_CONSOLE_DISPLAY_STATE as a pushed
/// notification, not from a poll — see PowerNotifications.</summary>
public sealed record SystemFlags(bool SessionLocked, bool RemoteSession, bool BatterySaver, bool D3DFullscreen,
    bool DisplayOff = false);

public enum PauseReason { None, Fullscreen, SessionLocked, RemoteSession, BatterySaver, DisplayOff }

/// <summary>Pure pause policy — no Win32 calls, fully unit-testable.</summary>
public static class PauseDecision
{
    private static readonly HashSet<string> ShellOrOwnClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "SHELLDLL_DefView", "SysListView32",
        "FeatherWallSurface", "FeatherWallClock", "FeatherWallMessage",
    };

    public const double CoverageThreshold = 0.95;

    public static bool IsShellOrOwnWindow(string className) => ShellOrOwnClasses.Contains(className);

    /// <summary>True when the foreground window effectively hides the desktop of the given
    /// monitor: maximized on it, or covering ≥95% of its work area.</summary>
    public static bool CoversMonitor(in RECT windowRect, in RECT monitorBounds, in RECT monitorWorkArea, bool isZoomed, IntPtr windowMonitor, IntPtr monitor)
    {
        if (isZoomed) return windowMonitor == monitor;
        if (!windowRect.IntersectsWith(monitorWorkArea)) return false;
        long covered = windowRect.IntersectionArea(monitorWorkArea);
        return covered >= monitorWorkArea.Area * CoverageThreshold;
    }

    public static PauseReason Evaluate(
        ForegroundInfo? foreground,
        in RECT monitorBounds,
        in RECT monitorWorkArea,
        IntPtr monitorHandle,
        SystemFlags flags,
        Config.PauseConfig config)
    {
        // Outranks everything, and is not configurable: there is no reading of "pause on
        // fullscreen: off" under which the user wants frames decoded into a dark panel.
        if (flags.DisplayOff) return PauseReason.DisplayOff;
        if (flags.SessionLocked) return PauseReason.SessionLocked;
        if (config.OnRemoteSession && flags.RemoteSession) return PauseReason.RemoteSession;
        if (config.OnBatterySaver && flags.BatterySaver) return PauseReason.BatterySaver;
        if (!config.OnFullscreen) return PauseReason.None;
        if (flags.D3DFullscreen) return PauseReason.Fullscreen;
        if (foreground is null || IsShellOrOwnWindow(foreground.ClassName)) return PauseReason.None;
        return CoversMonitor(foreground.WindowRect, monitorBounds, monitorWorkArea, foreground.IsZoomed, foreground.MonitorHandle, monitorHandle)
            ? PauseReason.Fullscreen
            : PauseReason.None;
    }
}
