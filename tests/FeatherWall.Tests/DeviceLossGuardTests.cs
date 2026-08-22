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
    public void ALossRaisedDuringRecovery_IsReportedByComplete()
    {
        // The surface built by the rebuild can be lost as fast as it is made. That loss arrives
        // while the guard is in flight, so it is rejected — and nothing else will ever raise it
        // again, because the dead surface never presents. Dropping it silently leaves a dead layer.
        var guard = new DeviceLossGuard();
        Assert.True(guard.TryBegin());

        Assert.False(guard.TryBegin()); // the mid-rebuild loss

        Assert.True(guard.Complete());  // ... which Complete hands back to the caller
    }

    [Fact]
    public void CompleteReportsAPendingLossOnlyOnce()
    {
        var guard = new DeviceLossGuard();
        guard.TryBegin();
        guard.TryBegin();
        Assert.True(guard.Complete());

        Assert.True(guard.TryBegin());
        Assert.False(guard.Complete()); // the flag did not survive into the next recovery
    }

    [Fact]
    public void ARecoveryWithNoFurtherLoss_ReportsNothingPending()
    {
        var guard = new DeviceLossGuard();
        Assert.True(guard.TryBegin());
        Assert.False(guard.Complete());
    }

    [Fact]
    public void ConcurrentBegins_AdmitExactlyOne()
    {
        // Every presenting surface reports the same lost device, from its own thread. Two winners
        // means two rebuilds on top of each other; a lost increment means the attempt budget the
        // class exists to enforce quietly grows.
        var guard = new DeviceLossGuard();
        int admitted = 0;
        using var start = new ManualResetEventSlim(false);

        var threads = Enumerable.Range(0, 16).Select(_ => new Thread(() =>
        {
            start.Wait();
            if (guard.TryBegin()) Interlocked.Increment(ref admitted);
        })).ToList();

        foreach (var t in threads) t.Start();
        start.Set();
        foreach (var t in threads) t.Join();

        Assert.Equal(1, admitted);
    }

    [Fact]
    public void ConcurrentBeginCompleteCycles_NeverExceedTheAttemptBudget()
    {
        var guard = new DeviceLossGuard(() => new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc));
        int admitted = 0;
        using var start = new ManualResetEventSlim(false);

        var threads = Enumerable.Range(0, 16).Select(_ => new Thread(() =>
        {
            start.Wait();
            for (int i = 0; i < 20; i++)
                if (guard.TryBegin()) { Interlocked.Increment(ref admitted); guard.Complete(); }
        })).ToList();

        foreach (var t in threads) t.Start();
        start.Set();
        foreach (var t in threads) t.Join();

        // The clock never advances, so the quiet period never refills the budget.
        Assert.Equal(DeviceLossGuard.MaxConsecutiveAttempts, admitted);
        Assert.True(guard.GaveUp);
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
