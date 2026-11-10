using System.Drawing;
using FeatherWall.Config;
using FeatherWall.Widgets;
using FeatherWall.Widgets.Themes;

namespace FeatherWall.Tests;

/// <summary>Shared fixtures for the group themes' layout tests.</summary>
internal static class Group
{
    public static readonly Rectangle Work = new(0, 0, 2560, 1540);
    public static readonly DateTime Noon = new(2026, 11, 10, 12, 30, 0);
    public static readonly NowPlayingReading Track = new("Bairan", "Banjaare", true, "t1", 0.42);
    public static readonly BatteryReading Battery = new(76, BatteryState.OnBattery);

    public static GroupInput Input(
        bool clock = true, bool track = true, bool battery = true, ClockAnchor? position = null,
        string title = "Bairan", float scale = 1f)
    {
        var config = new ClockConfig { Enabled = clock };
        return new GroupInput(Noon, config, new DiscConfig(), new HaloConfig(),
            track ? Track with { Title = title } : null,
            battery ? Battery : null,
            Color.FromArgb(255, 0xC0, 0x8B, 0xFF), scale, 1f, position, Work, 48);
    }

    public static Rectangle Union(IReadOnlyList<GroupPanel> panels) =>
        panels.Select(p => p.Bounds).Aggregate(Rectangle.Union);
}

public class OrbitTests
{
    private static readonly IGroupTheme Theme = GroupThemes.For(WidgetTheme.Orbit);

    [Fact]
    public void HourFraction_IsHowFarThroughTheHour()
    {
        Assert.Equal(0.5f, Orbit.HourFraction(new DateTime(2026, 1, 1, 12, 30, 0)), 3);
        Assert.Equal((45 * 60 + 30) / 3600f, Orbit.HourFraction(new DateTime(2026, 1, 1, 9, 45, 30)), 4);
    }

    [Fact]
    public void Everything_IsOnePanel_WithTheRecordBelowTheDial()
    {
        var panels = Theme.Layout(Group.Input());
        var panel = Assert.Single(panels);
        Assert.False(panel.FaceBox.IsEmpty);
        Assert.True(panel.FaceBox.Top > panel.Bounds.Height / 2, "the record ring sits under the dial");
    }

    [Fact]
    public void NoTrack_NoFace_AndShorter()
    {
        var all = Assert.Single(Theme.Layout(Group.Input()));
        var noTrack = Assert.Single(Theme.Layout(Group.Input(track: false)));
        Assert.True(noTrack.FaceBox.IsEmpty);
        Assert.True(noTrack.Bounds.Height < all.Bounds.Height);
    }

    [Fact]
    public void ClockOnly_IsJustTheDial()
    {
        var clockOnly = Assert.Single(Theme.Layout(Group.Input(track: false, battery: false)));
        var all = Assert.Single(Theme.Layout(Group.Input()));
        Assert.True(clockOnly.Bounds.Height < all.Bounds.Height);
    }

    [Fact]
    public void NothingToShow_IsNoPanel() =>
        Assert.Empty(Theme.Layout(Group.Input(clock: false, track: false, battery: false)));

    [Fact]
    public void DefaultsToTopLeft_AndHonoursPosition()
    {
        var topLeft = Assert.Single(Theme.Layout(Group.Input()));
        Assert.True(topLeft.Bounds.Left < 200 && topLeft.Bounds.Top < 200);

        var bottomRight = Assert.Single(Theme.Layout(Group.Input(position: ClockAnchor.BottomRight)));
        Assert.True(bottomRight.Bounds.Right > Group.Work.Right - 200);
        Assert.True(bottomRight.Bounds.Bottom > Group.Work.Bottom - 200);
        Assert.True(Group.Work.Contains(bottomRight.Bounds));
    }

    [Fact]
    public void LongTitle_DoesNotWidenTheColumn()
    {
        var shortTitle = Assert.Single(Theme.Layout(Group.Input()));
        var longTitle = Assert.Single(Theme.Layout(Group.Input(title: new string('W', 200))));
        Assert.Equal(shortTitle.Bounds.Size, longTitle.Bounds.Size);
    }

    [Fact]
    public void ScalesWithTheClock()
    {
        var one = Assert.Single(Theme.Layout(Group.Input()));
        var two = Assert.Single(Theme.Layout(Group.Input(scale: 2f)));
        Assert.InRange(two.Bounds.Width, one.Bounds.Width * 1.8, one.Bounds.Width * 2.2);
    }
}

public class PosterTests
{
    private static readonly IGroupTheme Theme = GroupThemes.For(WidgetTheme.Poster);
    private static GroupPanel Panel(IReadOnlyList<GroupPanel> panels, string key) => panels.Single(p => p.Key == key);

    [Fact]
    public void DefaultsToTopRight_RecordBottomLeft_BatteryBottomRight()
    {
        var panels = Theme.Layout(Group.Input());
        var clock = Panel(panels, "poster-clock");
        var record = Panel(panels, "poster-record");
        var battery = Panel(panels, "poster-battery");
        int midX = Group.Work.Width / 2, midY = Group.Work.Height / 2;
        Assert.True(clock.Bounds.Left > midX && clock.Bounds.Bottom < midY);
        Assert.True(record.Bounds.Right < midX && record.Bounds.Top > midY);
        Assert.True(battery.Bounds.Left > midX && battery.Bounds.Top > midY);
        Assert.False(record.FaceBox.IsEmpty);
    }

    [Fact]
    public void ClockAtBottomLeft_MirrorsTheOthersToTheTop()
    {
        var panels = Theme.Layout(Group.Input(position: ClockAnchor.BottomLeft));
        int midX = Group.Work.Width / 2, midY = Group.Work.Height / 2;
        Assert.True(Panel(panels, "poster-clock").Bounds.Top > midY);
        Assert.True(Panel(panels, "poster-record").Bounds.Left > midX && Panel(panels, "poster-record").Bounds.Bottom < midY);
        Assert.True(Panel(panels, "poster-battery").Bounds.Right < midX && Panel(panels, "poster-battery").Bounds.Bottom < midY);
    }

    [Fact]
    public void CentreAnchor_CountsAsTopRight()
    {
        var centre = Theme.Layout(Group.Input(position: ClockAnchor.Center));
        var topRight = Theme.Layout(Group.Input(position: ClockAnchor.TopRight));
        Assert.Equal(Panel(topRight, "poster-record").Bounds, Panel(centre, "poster-record").Bounds);
    }

    [Fact]
    public void AbsentPartsHaveNoPanel()
    {
        var panels = Theme.Layout(Group.Input(track: false, battery: false));
        Assert.Equal(["poster-clock"], panels.Select(p => p.Key));
        Assert.Empty(Theme.Layout(Group.Input(clock: false, track: false, battery: false)));
    }

    [Fact]
    public void LongTitle_StaysOnScreen()
    {
        var record = Panel(Theme.Layout(Group.Input(title: new string('W', 300))), "poster-record");
        Assert.True(Group.Work.Contains(record.Bounds));
        Assert.True(record.Bounds.Width < Group.Work.Width / 2);
    }
}

public class GlassDockTests
{
    private static readonly IGroupTheme Theme = GroupThemes.For(WidgetTheme.GlassDock);

    [Fact]
    public void OneCapsule_CentredAtTheBottom_ByDefault()
    {
        var dock = Assert.Single(Theme.Layout(Group.Input()));
        int centre = dock.Bounds.Left + dock.Bounds.Width / 2;
        Assert.InRange(centre, Group.Work.Width / 2 - 2, Group.Work.Width / 2 + 2);
        Assert.True(dock.Bounds.Top > Group.Work.Height / 2);
        Assert.False(dock.FaceBox.IsEmpty);
    }

    [Theory]
    [InlineData(ClockAnchor.TopLeft)]
    [InlineData(ClockAnchor.TopCenter)]
    [InlineData(ClockAnchor.TopRight)]
    public void AnyTopPosition_PutsItAtTheTopCentre(ClockAnchor position)
    {
        var dock = Assert.Single(Theme.Layout(Group.Input(position: position)));
        Assert.True(dock.Bounds.Bottom < Group.Work.Height / 2);
        int centre = dock.Bounds.Left + dock.Bounds.Width / 2;
        Assert.InRange(centre, Group.Work.Width / 2 - 2, Group.Work.Width / 2 + 2);
    }

    [Fact]
    public void HiddenSegments_Collapse()
    {
        var all = Assert.Single(Theme.Layout(Group.Input()));
        var noTrack = Assert.Single(Theme.Layout(Group.Input(track: false)));
        var clockOnly = Assert.Single(Theme.Layout(Group.Input(track: false, battery: false)));
        Assert.True(noTrack.Bounds.Width < all.Bounds.Width);
        Assert.True(clockOnly.Bounds.Width < noTrack.Bounds.Width);
        Assert.True(noTrack.FaceBox.IsEmpty);
    }

    [Fact]
    public void NothingEnabled_NoDock() =>
        Assert.Empty(Theme.Layout(Group.Input(clock: false, track: false, battery: false)));

    [Fact]
    public void LongTitle_DoesNotStretchTheDock()
    {
        var shortTitle = Assert.Single(Theme.Layout(Group.Input()));
        var longTitle = Assert.Single(Theme.Layout(Group.Input(title: new string('W', 200))));
        Assert.True(longTitle.Bounds.Width - shortTitle.Bounds.Width < Group.Work.Width / 5);
        Assert.True(Group.Work.Contains(longTitle.Bounds));
    }
}

public class PosterLineTests
{
    [Fact]
    public void LongTitle_IsCutShort_NotRunAcrossTheScreen()
    {
        var record = GroupThemes.For(WidgetTheme.Poster).Layout(Group.Input(title: new string('W', 93)))
            .Single(p => p.Key == "poster-record");
        Assert.True(record.Bounds.Width < Group.Work.Width / 4, $"record line {record.Bounds.Width}px wide");
    }
}
