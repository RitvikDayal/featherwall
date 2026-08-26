using FeatherWall.Interop;
using FeatherWall.Widgets;
using Xunit;

namespace FeatherWall.Tests;

/// <summary>What the battery widget shows, as a pure function of what Windows reports.
/// Null means "show nothing" — a desktop has no battery, and a line reading "N/A" is worse
/// than no line.</summary>
public class BatterySourceTests
{
    private static SYSTEM_POWER_STATUS Status(byte ac, byte flag, byte percent) =>
        new() { ACLineStatus = ac, BatteryFlag = flag, BatteryLifePercent = percent };

    [Fact]
    public void NoSystemBattery_ShowsNothing()
    {
        // BatteryFlag 128 is "no system battery" — every desktop PC.
        Assert.Null(BatterySource.Format(Status(ac: 1, flag: 128, percent: 255)));
    }

    [Fact]
    public void UnknownPercentage_ShowsNothing()
    {
        Assert.Null(BatterySource.Format(Status(ac: 0, flag: 1, percent: 255)));
    }

    [Fact]
    public void OnBattery_ShowsPercentAndState()
    {
        Assert.Equal("87% on battery", BatterySource.Format(Status(ac: 0, flag: 1, percent: 87)));
    }

    [Fact]
    public void Charging_SaysCharging()
    {
        Assert.Equal("87% charging", BatterySource.Format(Status(ac: 1, flag: 8, percent: 87)));
    }

    [Fact]
    public void FullOnAc_SaysCharged()
    {
        Assert.Equal("charged", BatterySource.Format(Status(ac: 1, flag: 1, percent: 100)));
    }
}

/// <summary>The registration list and the routing must agree. A GUID registered but claimed by
/// nobody is a subscription paid for and dropped — the defect found in the session display
/// state on 2026-08-22, which was registered for months and read nowhere.</summary>
public class PowerSettingRoutingTests
{
    [Fact]
    public void EveryRegisteredSettingIsClaimedBySomeConsumer()
    {
        foreach (var setting in PowerNotifications.RegisteredSettings)
        {
            bool claimed =
                PowerNotifications.IsDisplayState(setting) ||
                setting == PowerNotifications.AcDcPowerSource ||
                setting == PowerNotifications.PowerSavingStatus ||
                setting == PowerNotifications.BatteryPercentageRemaining;
            Assert.True(claimed, $"{setting} is registered but no consumer claims it");
        }
    }

    [Fact]
    public void BatteryPercentageGuid_MatchesTheSdkHeader()
    {
        // GUID_BATTERY_PERCENTAGE_REMAINING, winnt.h. A wrong value registers successfully and
        // then never fires, so nothing else in this suite would notice.
        Assert.Equal(new Guid("a7ad8041-b45a-4cae-87a3-eecbb468a9e1"),
                     PowerNotifications.BatteryPercentageRemaining);
    }

    [Fact]
    public void BatteryPercentage_IsNotTreatedAsADisplayState() =>
        Assert.False(PowerNotifications.IsDisplayState(PowerNotifications.BatteryPercentageRemaining));
}

/// <summary>What the now-playing widget shows, as a pure function of the session's properties.
/// The WinRT plumbing that supplies them is not testable here — it needs a live media session —
/// so this covers the half that is, and the wiring is verified by running the app.</summary>
public class NowPlayingSourceTests
{
    [Fact]
    public void NotPlaying_ShowsNothing() =>
        Assert.Null(NowPlayingSource.Format("Kind of Blue", "Miles Davis", isPlaying: false, 48));

    [Fact]
    public void NoTitle_ShowsNothing() =>
        Assert.Null(NowPlayingSource.Format("", "Miles Davis", isPlaying: true, 48));

    [Fact]
    public void TitleAndArtist_AreJoined() =>
        Assert.Equal("♪ Kind of Blue — Miles Davis",
            NowPlayingSource.Format("Kind of Blue", "Miles Davis", isPlaying: true, 48));

    [Fact]
    public void MissingArtist_ShowsTitleAlone() =>
        Assert.Equal("♪ Kind of Blue",
            NowPlayingSource.Format("Kind of Blue", "", isPlaying: true, 48));

    [Fact]
    public void OverlongText_IsTruncatedNotScrolled()
    {
        // Scrolling would mean animating, and animating means waking up.
        string result = NowPlayingSource.Format(new string('a', 100), "b", isPlaying: true, 20)!;
        Assert.Equal(20, result.Length);
        Assert.EndsWith("…", result);
    }
}

/// <summary>Cases CodeRabbit raised on PR #12: values Windows really returns, and config values
/// the JSON accepts even though the settings panel does not offer them.</summary>
public class WidgetSourceEdgeTests
{
    private static SYSTEM_POWER_STATUS Status(byte ac, byte flag, byte percent) =>
        new() { ACLineStatus = ac, BatteryFlag = flag, BatteryLifePercent = percent };

    [Fact]
    public void UnknownAcStatus_DoesNotClaimTheSource()
    {
        // ACLineStatus 255 means Windows cannot tell whether it is on mains. The charge is still
        // known, so the percentage is shown — but "on battery" would be an invented claim.
        Assert.Equal("87%", BatterySource.Format(Status(ac: 255, flag: 1, percent: 87)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TinyCharacterBudget_IsHonoured(int budget)
    {
        // maxCharacters below four used to be raised to four, so a budget of 1 produced four
        // characters. The settings panel's minimum is 10, but the JSON accepts anything.
        string? result = NowPlayingSource.Format("Kind of Blue", "Miles Davis", isPlaying: true, budget);
        if (budget <= 0) Assert.Null(result);
        else Assert.True(result!.Length <= budget, $"budget {budget} produced '{result}' ({result.Length})");
    }

    [Fact]
    public void BudgetOfOne_ShowsTheEllipsisAlone()
    {
        Assert.Equal("…", NowPlayingSource.Format("Kind of Blue", "Miles Davis", isPlaying: true, 1));
    }
}

/// <summary>The structured reading behind the text. Format is a rendering of this, so the halo's
/// ring and the widget's words cannot end up describing different states.</summary>
public class BatteryReadingTests
{
    private static SYSTEM_POWER_STATUS Status(byte ac, byte flag, byte percent) =>
        new() { ACLineStatus = ac, BatteryFlag = flag, BatteryLifePercent = percent };

    [Fact]
    public void NoBattery_ReadsAsNone() =>
        Assert.Equal(BatteryState.None, BatterySource.Read(Status(1, 128, 255)).State);

    [Fact]
    public void UnknownPercent_ReadsAsNone() =>
        Assert.Equal(BatteryState.None, BatterySource.Read(Status(0, 1, 255)).State);

    [Fact]
    public void OnAcBelowFull_ReadsAsCharging()
    {
        var r = BatterySource.Read(Status(1, 8, 87));
        Assert.Equal(BatteryState.Charging, r.State);
        Assert.Equal(87, r.Percent);
    }

    [Fact]
    public void OnAcAtFull_ReadsAsCharged() =>
        Assert.Equal(BatteryState.Charged, BatterySource.Read(Status(1, 1, 100)).State);

    [Fact]
    public void OffAc_ReadsAsOnBattery() =>
        Assert.Equal(BatteryState.OnBattery, BatterySource.Read(Status(0, 1, 42)).State);

    [Fact]
    public void UnknownAcStatus_ReadsAsUnknownSource()
    {
        // ACLineStatus 255 means Windows cannot tell. The level is still known.
        var r = BatterySource.Read(Status(255, 1, 87));
        Assert.Equal(BatteryState.UnknownSource, r.State);
        Assert.Equal(87, r.Percent);
    }
}

/// <summary>The structured now-playing reading. The record needs title, artist and playing state
/// separately — Format joins them into one string, which is the wrong shape for a widget that
/// draws them in different sizes and weights.
///
/// TrackId is what the album-art cache keys on: it must change when the track does and hold still
/// across a progress tick, or the art is either stale or refetched constantly.</summary>
public class NowPlayingReadingTests
{
    [Fact]
    public void NotPlaying_KeepsTheTitleAndSaysItIsPaused()
    {
        // The record stays on screen dimmed when you pause, still showing what you were
        // listening to. An earlier version nulled the title here, which emptied the widget at the
        // exact moment you look at it to see what stopped.
        var r = NowPlayingSource.Read("Kind of Blue", "Miles Davis", isPlaying: false);
        Assert.False(r.IsPlaying);
        Assert.Equal("Kind of Blue", r.Title);
    }

    [Fact]
    public void Playing_KeepsTitleAndArtistApart()
    {
        var r = NowPlayingSource.Read("Kind of Blue", "Miles Davis", isPlaying: true);
        Assert.True(r.IsPlaying);
        Assert.Equal("Kind of Blue", r.Title);
        Assert.Equal("Miles Davis", r.Artist);
    }

    [Fact]
    public void BlankArtist_BecomesNullRatherThanEmpty()
    {
        // The renderer decides whether to draw a second line by null-checking, not by measuring
        // an empty string that would still take a line's height.
        Assert.Null(NowPlayingSource.Read("Kind of Blue", "   ", isPlaying: true).Artist);
    }

    [Fact]
    public void TrackId_ChangesWithTheTrack()
    {
        var a = NowPlayingSource.Read("Blue in Green", "Miles Davis", isPlaying: true);
        var b = NowPlayingSource.Read("So What", "Miles Davis", isPlaying: true);
        Assert.NotEqual(a.TrackId, b.TrackId);
    }

    [Fact]
    public void TrackId_HoldsStillForTheSameTrack()
    {
        // Re-read on a playback tick must not invalidate the art cache.
        var a = NowPlayingSource.Read("Blue in Green", "Miles Davis", isPlaying: true);
        var b = NowPlayingSource.Read("Blue in Green", "Miles Davis", isPlaying: true);
        Assert.Equal(a.TrackId, b.TrackId);
    }

    [Fact]
    public void PauseDoesNotChangeTheTrackId()
    {
        // Pausing dims the record; it must not throw the artwork away and refetch it.
        var playing = NowPlayingSource.Read("Blue in Green", "Miles Davis", isPlaying: true);
        var paused = NowPlayingSource.Read("Blue in Green", "Miles Davis", isPlaying: false);
        Assert.Equal(playing.TrackId, paused.TrackId);
    }
}
