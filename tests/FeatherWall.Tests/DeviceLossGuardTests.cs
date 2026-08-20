using FeatherWall.Rendering;
using Xunit;

namespace FeatherWall.Tests;

/// <summary>The bound on GPU device-loss recovery. One driver reset raises the event from every
/// presenting surface, and a genuinely dead adapter loses every rebuilt device immediately —
/// so the policy is what stops an infinite rebuild loop, and it is tested without a GPU.</summary>
public class DeviceLossGuardTests
{
    private sealed class Clock
    {
        public DateTime Now = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        public DateTime Read() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    [Fact]
    public void FirstLoss_IsAccepted()
    {
        var guard = new DeviceLossGuard();
        Assert.True(guard.TryBegin());
    }

    [Fact]
    public void SecondLoss_WhileFirstInFlight_IsRejected()
    {
        // The real shape: several surfaces present, each reports the same lost device.
        var guard = new DeviceLossGuard();
        Assert.True(guard.TryBegin());
        Assert.False(guard.TryBegin());
        Assert.False(guard.TryBegin());
        Assert.False(guard.GaveUp); // rejected as duplicate, not as failure
    }

    [Fact]
    public void ConsecutiveLosses_GiveUpAfterTheBudget()
    {
        var clock = new Clock();
        var guard = new DeviceLossGuard(clock.Read);

        for (int i = 0; i < DeviceLossGuard.MaxConsecutiveAttempts; i++)
        {
            Assert.True(guard.TryBegin());
            guard.Complete();
            clock.Advance(TimeSpan.FromSeconds(1)); // immediately lost again
        }

        Assert.False(guard.TryBegin());
        Assert.True(guard.GaveUp);
    }

    [Fact]
    public void HavingGivenUp_StaysGivenUp_EvenAfterTheQuietPeriod()
    {
        var clock = new Clock();
        var guard = new DeviceLossGuard(clock.Read);
        for (int i = 0; i < DeviceLossGuard.MaxConsecutiveAttempts; i++) { guard.TryBegin(); guard.Complete(); }
        Assert.False(guard.TryBegin());

        clock.Advance(DeviceLossGuard.QuietPeriod * 5);

        Assert.False(guard.TryBegin());
        Assert.True(guard.GaveUp);
    }

    [Fact]
    public void ARecoveryThatSurvivesTheQuietPeriod_RefillsTheBudget()
    {
        // A driver update today and another next week each deserve the full allowance.
        var clock = new Clock();
        var guard = new DeviceLossGuard(clock.Read);

        Assert.True(guard.TryBegin());
        guard.Complete();

        clock.Advance(DeviceLossGuard.QuietPeriod + TimeSpan.FromSeconds(1));

        for (int i = 0; i < DeviceLossGuard.MaxConsecutiveAttempts; i++)
        {
            Assert.True(guard.TryBegin());
            guard.Complete();
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.False(guard.TryBegin());
    }

    [Fact]
    public void Reset_ClearsGaveUp_SoAnExplorerRestartCanRecoverAgain()
    {
        var guard = new DeviceLossGuard();
        for (int i = 0; i < DeviceLossGuard.MaxConsecutiveAttempts; i++) { guard.TryBegin(); guard.Complete(); }
        Assert.False(guard.TryBegin());
        Assert.True(guard.GaveUp);

        guard.Reset();

        Assert.False(guard.GaveUp);
        Assert.True(guard.TryBegin());
    }

    [Fact]
    public void CompleteWithoutBegin_DoesNotGrantAnExtraSlot()
    {
        var guard = new DeviceLossGuard();
        guard.Complete();
        Assert.True(guard.TryBegin());
        Assert.False(guard.TryBegin());
    }
}
