using FeatherWall.Config;
using FeatherWall.Interop;
using FeatherWall.Playback;
using FeatherWall.Widgets;
using Xunit;

namespace FeatherWall.Tests;

/// <summary>Pausing when the panel is dark. The state arrives pushed, via
/// GUID_CONSOLE_DISPLAY_STATE, so the only thing to test here is the policy: display-off
/// outranks every other reason and is not subject to config.</summary>
public class DisplayOffPauseTests
{
    private static readonly RECT Bounds = new(0, 0, 1920, 1080);
    private static readonly RECT Work = new(0, 0, 1920, 1040);
    private static readonly IntPtr Handle = new(1);

    private static PauseReason Evaluate(SystemFlags flags, PauseConfig? config = null, ForegroundInfo? foreground = null) =>
        PauseDecision.Evaluate(foreground, Bounds, Work, Handle, flags, config ?? new PauseConfig());

    [Fact]
    public void DisplayOff_Pauses()
    {
        var flags = new SystemFlags(false, false, false, false, DisplayOff: true);
        Assert.Equal(PauseReason.DisplayOff, Evaluate(flags));
    }

    [Fact]
    public void DisplayOff_IsNotConfigurable()
    {
        // Every opt-out switched off: there is still no reading under which the user wants
        // frames decoded into a monitor that is powered down.
        var config = new PauseConfig { OnFullscreen = false, OnBatterySaver = false, OnRemoteSession = false };
        var flags = new SystemFlags(false, false, false, false, DisplayOff: true);
        Assert.Equal(PauseReason.DisplayOff, Evaluate(flags, config));
    }

    [Fact]
    public void DisplayOff_OutranksSessionLock()
    {
        var flags = new SystemFlags(SessionLocked: true, false, false, false, DisplayOff: true);
        Assert.Equal(PauseReason.DisplayOff, Evaluate(flags));
    }

    [Fact]
    public void DisplayOn_LeavesEveryOtherReasonUnchanged()
    {
        // The regression guard: adding a flag must not perturb the existing table.
        Assert.Equal(PauseReason.None, Evaluate(new SystemFlags(false, false, false, false)));
        Assert.Equal(PauseReason.SessionLocked, Evaluate(new SystemFlags(true, false, false, false)));
        Assert.Equal(PauseReason.RemoteSession, Evaluate(new SystemFlags(false, true, false, false)));
        Assert.Equal(PauseReason.BatterySaver, Evaluate(new SystemFlags(false, false, true, false)));
        Assert.Equal(PauseReason.Fullscreen, Evaluate(new SystemFlags(false, false, false, true)));
    }

    [Fact]
    public void DefaultSystemFlags_HaveTheDisplayOn()
    {
        // The parameter is optional so existing call sites compile; that must not mean "off".
        Assert.False(new SystemFlags(false, false, false, false).DisplayOff);
    }

    // ---- the tick the suspension removes ----------------------------------------------------

    [Fact]
    public void WithoutSeconds_TheClockTicksOncePerMinute_Not86400TimesADay()
    {
        var now = new DateTime(2026, 8, 20, 13, 45, 10, 500);
        int ms = ClockLayout.MillisecondsToNextTick(now, showSeconds: false);

        Assert.Equal((59 - 10) * 1000 + 500, ms);
        Assert.True(ms > 1000, "a minute clock must not re-arm every second");
    }

    [Fact]
    public void WithSeconds_TheClockTicksOnTheSecondBoundary()
    {
        var now = new DateTime(2026, 8, 20, 13, 45, 10, 500);
        Assert.Equal(500, ClockLayout.MillisecondsToNextTick(now, showSeconds: true));
    }

    [Fact]
    public void TickIsNeverZeroOrNegative_SoTheTimerCannotSpin()
    {
        var onTheBoundary = new DateTime(2026, 8, 20, 13, 45, 59, 999);
        Assert.True(ClockLayout.MillisecondsToNextTick(onTheBoundary, showSeconds: true) >= 15);
        Assert.True(ClockLayout.MillisecondsToNextTick(onTheBoundary, showSeconds: false) >= 15);
    }

    [Fact]
    public void ConsoleDisplayStateGuid_IsTheDocumentedOne_NotTheSupersededMonitorPowerOn()
    {
        // GUID_MONITOR_POWER_ON (02731015-4510-4526-99e6-e5a17ebd1aea) is superseded and
        // primary-monitor-only. Pinned so nobody "restores" it from a stale tutorial.
        Assert.Equal(new Guid("6fe69556-704a-47a0-8f24-c28d936fda47"), PowerNotifications.ConsoleDisplayState);
        Assert.NotEqual(new Guid("02731015-4510-4526-99e6-e5a17ebd1aea"), PowerNotifications.ConsoleDisplayState);
    }

    [Fact]
    public void TryRead_RejectsANullPayload_RatherThanGuessing()
    {
        Assert.False(PowerNotifications.TryRead(IntPtr.Zero, out _, out _));
    }
}
