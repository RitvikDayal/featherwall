using System.Globalization;
using System.Drawing;
using FeatherWall.Config;
using FeatherWall.Widgets.Themes;

namespace FeatherWall.Tests;

public class MonolithTests
{
    private static readonly IGroupTheme Theme = GroupThemes.For(WidgetTheme.Monolith);

    [Fact]
    public void IsAGroupTheme() => Assert.True(WidgetsConfig.IsGroup(WidgetTheme.Monolith));

    [Fact]
    public void DayAndStrip_AreTwoPanels_WithTheRecordInTheStrip()
    {
        var panels = Theme.Layout(Group.Input());
        Assert.Equal(2, panels.Count);
        var day = Assert.Single(panels, p => p.Key == "monolith-day");
        var strip = Assert.Single(panels, p => p.Key == "monolith-strip");
        Assert.True(day.FaceBox.IsEmpty);
        Assert.False(strip.FaceBox.IsEmpty);
    }

    [Fact]
    public void DefaultsToTopLeft_WithTheStripAtTheBottom()
    {
        var panels = Theme.Layout(Group.Input());
        var day = panels.Single(p => p.Key == "monolith-day");
        var strip = panels.Single(p => p.Key == "monolith-strip");
        Assert.True(day.Bounds.Left < 200 && day.Bounds.Top < 200);
        Assert.True(strip.Bounds.Bottom > Group.Work.Bottom - 200);
        Assert.Equal(day.Bounds.Left, strip.Bounds.Left);
    }

    [Fact]
    public void BottomPosition_PutsTheStripAtTheTop()
    {
        var panels = Theme.Layout(Group.Input(position: ClockAnchor.BottomRight));
        var day = panels.Single(p => p.Key == "monolith-day");
        var strip = panels.Single(p => p.Key == "monolith-strip");
        Assert.True(day.Bounds.Bottom > Group.Work.Bottom - 200);
        Assert.True(strip.Bounds.Top < 200);
        Assert.True(panels.All(p => Group.Work.Contains(p.Bounds)));
    }

    [Fact]
    public void NoTrackNoBattery_IsJustTheDay() =>
        Assert.Equal("monolith-day", Assert.Single(Theme.Layout(Group.Input(track: false, battery: false))).Key);

    [Fact]
    public void BatteryOnly_StripHasNoRecord()
    {
        var strip = Theme.Layout(Group.Input(track: false)).Single(p => p.Key == "monolith-strip");
        Assert.True(strip.FaceBox.IsEmpty);
    }

    [Fact]
    public void NothingToShow_IsNoPanel() =>
        Assert.Empty(Theme.Layout(Group.Input(clock: false, track: false, battery: false)));

    [Fact]
    public void LongTitle_IsCutToAFixedWidth()
    {
        var shortStrip = Theme.Layout(Group.Input()).Single(p => p.Key == "monolith-strip");
        var longStrip = Theme.Layout(Group.Input(title: new string('W', 200))).Single(p => p.Key == "monolith-strip");
        Assert.True(longStrip.Bounds.Width < Group.Work.Width / 2);
        Assert.True(longStrip.Bounds.Width >= shortStrip.Bounds.Width);
    }

    [Fact]
    public void ScalesWithTheClock()
    {
        var one = Theme.Layout(Group.Input()).Single(p => p.Key == "monolith-day");
        var two = Theme.Layout(Group.Input(scale: 2f)).Single(p => p.Key == "monolith-day");
        Assert.InRange(two.Bounds.Height, one.Bounds.Height * 1.8, one.Bounds.Height * 2.2);
    }

    [Fact]
    public void EveryPanel_Paints()
    {
        foreach (var panel in Theme.Layout(Group.Input()))
        {
            using var bmp = new Bitmap(panel.Bounds.Width, panel.Bounds.Height);
            using var g = Graphics.FromImage(bmp);
            panel.Paint(g);
        }
    }

    /// <summary>The review's case: Portuguese "QUARTA-FEIRA" at size 300%, and English "WEDNESDAY"
    /// larger still, ran the weekday off the screen at every anchor.</summary>
    [Theory]
    [InlineData("pt-BR", 3f)]
    [InlineData("en-US", 4.5f)]
    public void LongWeekday_StaysOnScreen_AtEveryPosition(string cultureName, float scale)
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(cultureName);
        try
        {
            var wednesday = new DateTime(2026, 11, 11, 12, 30, 0);
            var laptop = new Rectangle(0, 0, 1920, 1032);
            foreach (var anchor in Enum.GetValues<ClockAnchor>())
            {
                var day = Theme.Layout(Group.Input(position: anchor, scale: scale) with { Now = wednesday, WorkArea = laptop })
                    .Single(p => p.Key == "monolith-day");
                Assert.True(day.Bounds.Left >= laptop.Left && day.Bounds.Right <= laptop.Right,
                    $"{anchor}: {day.Bounds}");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void LongTitle_StaysWithinTheScreen_AtLargeSizes()
    {
        foreach (var anchor in Enum.GetValues<ClockAnchor>())
        {
            var strip = Theme.Layout(Group.Input(position: anchor, title: new string('W', 200), scale: 4.5f))
                .Single(p => p.Key == "monolith-strip");
            Assert.True(strip.Bounds.Left >= Group.Work.Left && strip.Bounds.Right <= Group.Work.Right,
                $"{anchor}: {strip.Bounds}");
            Assert.True(strip.Bounds.Width <= Group.Work.Width / 2, $"{anchor}: {strip.Bounds.Width}");
        }
    }
}

public class TilesTests
{
    private static readonly IGroupTheme Theme = GroupThemes.For(WidgetTheme.Tiles);

    [Fact]
    public void IsAGroupTheme() => Assert.True(WidgetsConfig.IsGroup(WidgetTheme.Tiles));

    [Fact]
    public void Everything_IsOnePanel_WithTheRecordUnderTheClock()
    {
        var panel = Assert.Single(Theme.Layout(Group.Input()));
        Assert.False(panel.FaceBox.IsEmpty);
        Assert.True(panel.FaceBox.Top > panel.Bounds.Height / 3, "the music tile sits under the clock and date tiles");
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
    public void NoBattery_IsShorter()
    {
        var all = Assert.Single(Theme.Layout(Group.Input()));
        var noBattery = Assert.Single(Theme.Layout(Group.Input(battery: false)));
        Assert.True(noBattery.Bounds.Height < all.Bounds.Height);
    }

    [Fact]
    public void NothingToShow_IsNoPanel() =>
        Assert.Empty(Theme.Layout(Group.Input(clock: false, track: false, battery: false)));

    [Fact]
    public void DefaultsToBottomRight_AndHonoursPosition()
    {
        var bottomRight = Assert.Single(Theme.Layout(Group.Input()));
        Assert.True(bottomRight.Bounds.Right > Group.Work.Right - 200);
        Assert.True(bottomRight.Bounds.Bottom > Group.Work.Bottom - 200);

        var topLeft = Assert.Single(Theme.Layout(Group.Input(position: ClockAnchor.TopLeft)));
        Assert.True(topLeft.Bounds.Left < 200 && topLeft.Bounds.Top < 200);
        Assert.True(Group.Work.Contains(topLeft.Bounds));
    }

    [Fact]
    public void LongTitle_DoesNotWidenTheStack()
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

    [Fact]
    public void HandAngles_ReadTheTime()
    {
        var (hour, minute) = Tiles.HandAngles(new DateTime(2026, 1, 1, 3, 0, 0));
        Assert.Equal(90f, hour, 2);
        Assert.Equal(0f, minute, 2);
        (hour, minute) = Tiles.HandAngles(new DateTime(2026, 1, 1, 12, 30, 0));
        Assert.Equal(15f, hour, 2);
        Assert.Equal(180f, minute, 2);
    }

    [Fact]
    public void Paints()
    {
        var panel = Assert.Single(Theme.Layout(Group.Input()));
        using var bmp = new Bitmap(panel.Bounds.Width, panel.Bounds.Height);
        using var g = Graphics.FromImage(bmp);
        panel.Paint(g);
    }
}
