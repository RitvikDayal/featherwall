using PrismOS.Rendering;

namespace PrismOS.Tests;

/// <summary>The lock→unlock ease used to be untested maths buried in a renderer. These pin the
/// curve's shape, because the whole effect is a lie if the curve overshoots, stalls or ends
/// somewhere other than neutral.</summary>
public class WakeEaseTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(-0.5f, 0f)]
    [InlineData(1.5f, 1f)]
    public void CubicBezier_PinsTheEndpoints(float x, float expected) =>
        Assert.Equal(expected, WakeEase.CubicBezier(x), 3);

    [Fact]
    public void CubicBezier_IsMonotonic_AndInsideTheUnitRange()
    {
        float previous = -1f;
        for (int i = 0; i <= 100; i++)
        {
            float eased = WakeEase.CubicBezier(i / 100f);
            Assert.InRange(eased, 0f, 1f);
            Assert.True(eased >= previous, $"ease went backwards at x={i / 100f:0.00}: {eased} < {previous}");
            previous = eased;
        }
    }

    [Fact]
    public void CubicBezier_IsFrontLoaded_LikeTheWinUiUnlockCurve()
    {
        // cubic-bezier(0.22, 1, 0.36, 1) pulls both control points to y=1: most of the motion
        // happens in the first third and the last stretch is a settle, not a slide.
        Assert.True(WakeEase.CubicBezier(0.25f) > 0.7f);
        Assert.True(WakeEase.CubicBezier(0.5f) > 0.9f);
    }

    [Fact]
    public void FrozenState_IsTheDocumentedScaleAndDim()
    {
        // These are the numbers the lock screen's still has to match: a 4% scale-in with the
        // picture dimmed to 88%.
        Assert.Equal(1.04f, WakeEase.StartScale);
        Assert.Equal(0.88f, WakeEase.StartDim);
        Assert.Equal(1f, WakeEase.EndScale);
        Assert.Equal(1f, WakeEase.EndDim);
    }
}
