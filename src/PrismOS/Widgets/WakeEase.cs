using PrismOS.Rendering;

namespace PrismOS.Widgets;

/// <summary>The lock-to-unlock wake ease: a scale-back from 1.04 to 1.0 and a brightness ease
/// from 0.88 to 1.0, both along the CSS cubic-bezier(0.22, 1, 0.36, 1) timing in a 33 ms tick
/// timer. The state is stored on the effect's own timer — a re-lock mid-ramp cancels it.</summary>
public sealed class WakeEase : IDisposable
{
    public const float StartScale = 1.04f;
    public const float EndScale = 1.0f;
    public const float StartDim = 0.88f;
    public const float EndDim = 1.0f;

    private const float DurationMs = 1400f;
    private const int StartDelayMs = 350;
    private const int TickMs = 33;

    private readonly CompositionSurface _surface;
    private readonly CompositionHost _host;
    private readonly int _width;
    private readonly int _height;
    private System.Threading.Timer _timer;
    private bool _started;
    private long _startTicks;
    private bool _disposed;

    public WakeEase(CompositionSurface surface, CompositionHost host, int width, int height)
    {
        _surface = surface;
        _host = host;
        _width = width;
        _height = height;
        _timer = null!;
    }

    /// <summary>Lock: snap to the frozen state (scale 1.04, dim 0.88) with no easing.</summary>
    public void ApplyLocked() => ApplyState(StartScale, StartDim);

    /// <summary>Unlock: start easing back to 1.0. A 350 ms delay matches the Windows lock
    /// fade-out before any motion is perceived.</summary>
    public void Start()
    {
        if (_disposed) return;
        Cancel(); // a previous ramp still running? cancel to keep one owner.
        _started = false;
        _startTicks = 0;
        _timer = new System.Threading.Timer(_ => Tick(), null, StartDelayMs, TickMs);
    }

    /// <summary>Stop any running ramp and leave the visual at the final state.</summary>
    public void SnapToFinal()
    {
        Cancel();
        ApplyState(EndScale, EndDim);
    }

    public void Cancel()
    {
        if (_timer is not null)
        {
            try { _timer.Dispose(); } catch { }
            _timer = null!;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Cancel();
    }

    private void Tick()
    {
        if (_disposed) return;
        if (!_started)
        {
            _started = true;
            _startTicks = Environment.TickCount64;
            return;
        }
        long elapsed = Environment.TickCount64 - _startTicks;
        float t = Math.Clamp(elapsed / DurationMs, 0f, 1f);
        float eased = CubicBezier(t);
        float scale = StartScale + (EndScale - StartScale) * eased;
        float dim = StartDim + (EndDim - StartDim) * eased;
        ApplyState(scale, dim);
        if (t >= 1f)
        {
            Cancel();
            ApplyState(EndScale, EndDim);
        }
    }

    private void ApplyState(float scale, float dim)
    {
        try
        {
            _host.MutateTree(() =>
            {
                var t = _host.Factories.DComp.CreateScaleTransform();
                t.SetScaleX(scale);
                t.SetScaleY(scale);
                t.SetCenterX(_width / 2f);
                t.SetCenterY(_height / 2f);
                _surface.Visual.SetTransform(t);

                var v3 = _surface.Visual.QueryInterface<Vortice.DirectComposition.IDCompositionVisual3>();
                v3?.SetOpacity(dim);
                v3?.Dispose();
            });
        }
        catch (Exception ex)
        {
            Common.Log.Warn($"WakeEase step failed: {ex.Message}");
        }
    }

    /// <summary>CSS cubic-bezier(0.22, 1, 0.36, 1): maps time x ∈ [0,1] to eased progress y.</summary>
    public static float CubicBezier(float x)
    {
        const float x1 = 0.22f, y1 = 1.0f, x2 = 0.36f, y2 = 1.0f;
        if (x <= 0f) return 0f;
        if (x >= 1f) return 1f;

        // Invert the x-axis bezier: given x, find the parameter t where Bx(t) = x.
        float lo = 0f, hi = 1f;
        for (int i = 0; i < 24; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (CurveAt(mid, x1, x2) < x) lo = mid; else hi = mid;
        }
        return CurveAt((lo + hi) * 0.5f, y1, y2);

        static float CurveAt(float t, float c1, float c2)
        {
            float u = 1f - t;
            return 3f * c1 * u * u * t + 3f * c2 * u * t * t + t * t * t;
        }
    }
}
