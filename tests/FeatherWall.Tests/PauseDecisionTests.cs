using FeatherWall.Config;
using FeatherWall.Interop;
using FeatherWall.Playback;

namespace FeatherWall.Tests;

public class PauseDecisionTests
{
    private static readonly RECT Bounds = new(0, 0, 2560, 1600);
    private static readonly RECT Work = new(0, 0, 2560, 1552);
    private static readonly IntPtr Monitor = new(0x1111);
    private static readonly PauseConfig Defaults = new();

    private static ForegroundInfo App(RECT rect, bool zoomed = false, IntPtr? monitor = null, string cls = "Chrome_WidgetWin_1") =>
        new(cls, rect, zoomed, monitor ?? Monitor);

    [Fact]
    public void NoForeground_NoPause() =>
        Assert.Equal(PauseReason.None,
            PauseDecision.Evaluate(null, Bounds, Work, Monitor, new SystemFlags(false, false, false, false), Defaults));

    [Fact]
    public void SmallWindow_NoPause() =>
        Assert.Equal(PauseReason.None,
            PauseDecision.Evaluate(App(new RECT(100, 100, 900, 700)), Bounds, Work, Monitor,
                new SystemFlags(false, false, false, false), Defaults));

    [Fact]
    public void MaximizedWindow_OnSameMonitor_Pauses() =>
        Assert.Equal(PauseReason.Fullscreen,
            PauseDecision.Evaluate(App(new RECT(-8, -8, 2568, 1560), zoomed: true), Bounds, Work, Monitor,
                new SystemFlags(false, false, false, false), Defaults));

    [Fact]
    public void MaximizedWindow_OnOtherMonitor_DoesNotPause() =>
        Assert.Equal(PauseReason.None,
            PauseDecision.Evaluate(App(new RECT(2560, 0, 4480, 1080), zoomed: true, monitor: new IntPtr(0x2222)),
                Bounds, Work, Monitor, new SystemFlags(false, false, false, false), Defaults));

    [Fact]
    public void BorderlessFullscreen_CoveringWorkArea_Pauses() =>
        Assert.Equal(PauseReason.Fullscreen,
            PauseDecision.Evaluate(App(new RECT(0, 0, 2560, 1600)), Bounds, Work, Monitor,
                new SystemFlags(false, false, false, false), Defaults));

    [Fact]
    public void NinetyFourPercentCoverage_DoesNotPause()
    {
        // 94% of the work area — just under the 95% threshold
        var rect = new RECT(0, 0, 2560, (int)(1552 * 0.94));
        Assert.Equal(PauseReason.None,
            PauseDecision.Evaluate(App(rect), Bounds, Work, Monitor,
                new SystemFlags(false, false, false, false), Defaults));
    }

    [Fact]
    public void ShellWindow_NeverPauses() =>
        Assert.Equal(PauseReason.None,
            PauseDecision.Evaluate(App(new RECT(0, 0, 2560, 1600), cls: "WorkerW"), Bounds, Work, Monitor,
                new SystemFlags(false, false, false, false), Defaults));

    [Fact]
    public void OwnWallpaperWindow_NeverPauses() =>
        Assert.Equal(PauseReason.None,
            PauseDecision.Evaluate(App(new RECT(0, 0, 2560, 1600), cls: "FeatherWallSurface"), Bounds, Work, Monitor,
                new SystemFlags(false, false, false, false), Defaults));

    [Fact]
    public void SessionLock_TrumpsEverything() =>
        Assert.Equal(PauseReason.SessionLocked,
            PauseDecision.Evaluate(null, Bounds, Work, Monitor, new SystemFlags(true, false, false, false), Defaults));

    [Fact]
    public void BatterySaver_PausesWhenEnabled() =>
        Assert.Equal(PauseReason.BatterySaver,
            PauseDecision.Evaluate(null, Bounds, Work, Monitor, new SystemFlags(false, false, true, false), Defaults));

    [Fact]
    public void BatterySaver_IgnoredWhenDisabled() =>
        Assert.Equal(PauseReason.None,
            PauseDecision.Evaluate(null, Bounds, Work, Monitor, new SystemFlags(false, false, true, false),
                new PauseConfig { OnBatterySaver = false }));

    [Fact]
    public void D3DExclusiveFullscreen_Pauses() =>
        Assert.Equal(PauseReason.Fullscreen,
            PauseDecision.Evaluate(null, Bounds, Work, Monitor, new SystemFlags(false, false, false, true), Defaults));

    [Fact]
    public void FullscreenDetection_Disabled_NoPause() =>
        Assert.Equal(PauseReason.None,
            PauseDecision.Evaluate(App(new RECT(0, 0, 2560, 1600), zoomed: true), Bounds, Work, Monitor,
                new SystemFlags(false, false, false, true), new PauseConfig { OnFullscreen = false }));
}
