using System.Diagnostics;
using FeatherWall.Common;
using FeatherWall.Widgets;
using Vortice.DirectComposition;

namespace FeatherWall.Rendering;

/// <summary>Turns one visual at 33⅓ rpm on the compositor. A repeating DirectComposition
/// animation drives a rotate transform, so DWM advances the angle every frame and this process
/// never wakes for it — no timer, no redraw, no present.
///
/// The cost does not vanish; it moves into dwm.exe, which recomposes the turning region at the
/// display's refresh rate. Whether that is cheaper than redrawing 15 times a second is measured
/// (docs/benchmark.md), not assumed.</summary>
public sealed class CompositorSpin : IDisposable
{
    private readonly CompositionHost _host;
    private readonly IDCompositionRotateTransform _rotate;
    private readonly Stopwatch _clock = new();
    private IDCompositionAnimation? _animation;
    private float _from;

    public bool Running => _animation is not null;

    public CompositorSpin(CompositionHost host, IDCompositionVisual visual, float centerX, float centerY)
    {
        _host = host;
        _rotate = host.Factories.DComp.CreateRotateTransform();
        host.MutateTree(() =>
        {
            _rotate.SetCenterX(centerX);
            _rotate.SetCenterY(centerY);
            visual.SetTransform(_rotate);
        });
        Log.Info("Record: compositor spin created");
    }

    /// <summary>Starts turning from <paramref name="fromDegrees"/>. One linear segment over a single
    /// turn, repeated: the angle runs from..from+360 and jumps back to from, which is the same
    /// place.</summary>
    public void Start(float fromDegrees)
    {
        if (Running) return;
        var animation = _host.Factories.DComp.CreateAnimation();
        animation.AddCubic(0, fromDegrees, RecordSpin.DegreesPerSecond, 0, 0);
        animation.AddRepeat(RecordSpin.SecondsPerTurn, RecordSpin.SecondsPerTurn);
        _host.MutateTree(() =>
        {
            _rotate.SetAngle(animation);
            _clock.Restart();
        });
        _from = fromDegrees;
        _animation = animation;
    }

    /// <summary>Sets a still angle on a spin that is not turning. A rebuilt record — new size, new
    /// layers — starts at 0° otherwise, so a paused one jumped back each time its panel was rebuilt.</summary>
    public void Hold(float degrees)
    {
        if (Running) return;
        _host.MutateTree(() => _rotate.SetAngle(degrees));
        _from = degrees;
    }

    /// <summary>Holds the record where it has got to and returns that angle, so the next Start
    /// carries on from it rather than snapping back to 0°.</summary>
    public float Stop()
    {
        if (_animation is null) return _from;
        float angle = RecordSpin.AngleAt(_from, _clock.Elapsed);
        _host.MutateTree(() => _rotate.SetAngle(angle));
        _animation.Dispose();
        _animation = null;
        _from = angle;
        return angle;
    }

    public void Dispose()
    {
        try { Stop(); } catch { /* the tree may already be gone */ }
        _rotate.Dispose();
    }
}
