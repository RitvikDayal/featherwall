namespace FeatherWall.Widgets;

/// <summary>How fast the record turns and when it may. Pure, so the gate that keeps the turn from
/// costing anything while nobody can see it is tested rather than argued.</summary>
public static class RecordSpin
{
    /// <summary>33⅓ rpm, as an LP: 0.5556 turns a second.</summary>
    public const float DegreesPerSecond = 200f;

    public const double SecondsPerTurn = 1.8;

    /// <summary>The timer path's frame rate. A record at 33⅓ rpm turns 13° per frame at this rate,
    /// which reads as motion; going higher spends frames nobody can see on a wallpaper.</summary>
    public const int TimerFramesPerSecond = 15;

    /// <summary>Where a record that started at <paramref name="startDegrees"/> has got to after
    /// <paramref name="elapsed"/>, in [0, 360).</summary>
    public static float AngleAt(float startDegrees, TimeSpan elapsed)
    {
        double angle = (startDegrees + DegreesPerSecond * elapsed.TotalSeconds) % 360.0;
        if (angle < 0) angle += 360.0;
        return angle >= 360.0 ? 0f : (float)angle;
    }

    /// <summary>The record turns only while all of these hold. Any one failing stops it dead
    /// rather than throttling it.</summary>
    public static bool ShouldTurn(bool disposed, bool suspended, bool covered,
                                  bool rotate, bool playing, bool hasSize) =>
        !disposed && !suspended && !covered && rotate && playing && hasSize;
}
