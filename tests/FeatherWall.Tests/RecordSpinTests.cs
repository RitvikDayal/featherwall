using FeatherWall.Widgets;

namespace FeatherWall.Tests;

/// <summary>The record's turn, as maths and as a gate. Whichever thing drives the turn — a timer
/// or the compositor — the angle it stops at and the conditions it runs under come from here.</summary>
public class RecordSpinTests
{
    [Theory]
    [InlineData(0f, 0.0, 0f)]
    [InlineData(0f, 0.9, 180f)]
    [InlineData(0f, 1.8, 0f)]      // a whole turn comes back to the start
    [InlineData(350f, 0.1, 10f)]   // wraps past 360
    [InlineData(90f, 3.6, 90f)]    // two whole turns
    public void AngleAt_Turns200DegreesASecond(float start, double seconds, float expected)
    {
        Assert.Equal(expected, RecordSpin.AngleAt(start, TimeSpan.FromSeconds(seconds)), precision: 3);
    }

    [Fact]
    public void AngleAt_StaysWithinOneTurn()
    {
        for (double s = 0; s < 20; s += 0.37)
        {
            float a = RecordSpin.AngleAt(123f, TimeSpan.FromSeconds(s));
            Assert.InRange(a, 0f, 359.9999f);
        }
    }

    [Fact]
    public void ShouldTurn_WhenEverythingHolds()
    {
        Assert.True(RecordSpin.ShouldTurn(disposed: false, suspended: false, covered: false,
                                          rotate: true, playing: true, hasSize: true));
    }

    [Theory]
    [InlineData(true, false, false, true, true, true)]   // disposed
    [InlineData(false, true, false, true, true, true)]   // display off
    [InlineData(false, false, true, true, true, true)]   // desktop covered
    [InlineData(false, false, false, false, true, true)] // Rotate off in settings
    [InlineData(false, false, false, true, false, true)] // paused or stopped
    [InlineData(false, false, false, true, true, false)] // nothing laid out
    public void ShouldTurn_AnyConditionFailingStopsIt(bool disposed, bool suspended, bool covered,
                                                      bool rotate, bool playing, bool hasSize)
    {
        Assert.False(RecordSpin.ShouldTurn(disposed, suspended, covered, rotate, playing, hasSize));
    }
}
