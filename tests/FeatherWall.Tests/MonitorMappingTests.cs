using FeatherWall.Desktop;
using FeatherWall.Interop;

namespace FeatherWall.Tests;

public class MonitorMappingTests
{
    [Fact]
    public void PrimaryMonitor_WithParentAtOrigin_IsIdentity()
    {
        var monitor = new RECT(0, 0, 2560, 1600);
        var parent = new RECT(0, 0, 2560, 1600);
        var r = MonitorTracker.ScreenToParentClient(monitor, parent);
        Assert.Equal(new RECT(0, 0, 2560, 1600), r);
    }

    [Fact]
    public void MonitorLeftOfPrimary_MapsToPositiveParentCoords()
    {
        // Virtual screen spans (-1920,0)..(2560,1600); parent (WorkerW) covers all of it.
        var monitor = new RECT(-1920, 0, 0, 1080);
        var parent = new RECT(-1920, 0, 2560, 1600);
        var r = MonitorTracker.ScreenToParentClient(monitor, parent);
        Assert.Equal(new RECT(0, 0, 1920, 1080), r);
    }

    [Fact]
    public void SecondaryBelowPrimary_KeepsSize()
    {
        var monitor = new RECT(0, 1600, 1920, 2680);
        var parent = new RECT(0, 0, 2560, 2680);
        var r = MonitorTracker.ScreenToParentClient(monitor, parent);
        Assert.Equal(new RECT(0, 1600, 1920, 2680), r);
        Assert.Equal(monitor.Width, r.Width);
        Assert.Equal(monitor.Height, r.Height);
    }

    [Fact]
    public void RectHelpers_AreaAndIntersection()
    {
        var a = new RECT(0, 0, 100, 100);
        var b = new RECT(50, 50, 150, 150);
        Assert.Equal(10_000, a.Area);
        Assert.True(a.IntersectsWith(b));
        Assert.Equal(2_500, a.IntersectionArea(b));
        Assert.Equal(0, a.IntersectionArea(new RECT(200, 200, 300, 300)));
    }
}
