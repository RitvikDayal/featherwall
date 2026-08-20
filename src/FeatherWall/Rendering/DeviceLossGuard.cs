namespace FeatherWall.Rendering;

/// <summary>Rate-limits GPU device-loss recovery.
///
/// A lost device is reported by every surface that presents, so one driver reset raises the
/// event several times in a row; and if the adapter is genuinely gone, every rebuilt device is
/// lost again immediately. Without a bound that is an infinite rebuild loop that pins a core
/// and fills the log. This holds the policy so it can be tested without a GPU.
///
/// The budget refills once a recovery survives <see cref="QuietPeriod"/>, so a driver update
/// today and another next week each get the full allowance, while a dying adapter gives up.</summary>
public sealed class DeviceLossGuard
{
    public const int MaxConsecutiveAttempts = 3;
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromMinutes(1);

    private readonly Func<DateTime> _now;
    private int _consecutive;
    private DateTime _lastAttempt = DateTime.MinValue;
    private bool _inFlight;

    public DeviceLossGuard(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    public bool GaveUp { get; private set; }

    /// <summary>True if the caller now owns a recovery and must call <see cref="Complete"/>.</summary>
    public bool TryBegin()
    {
        if (_inFlight || GaveUp) return false;

        var now = _now();
        if (now - _lastAttempt >= QuietPeriod) _consecutive = 0;

        if (_consecutive >= MaxConsecutiveAttempts)
        {
            GaveUp = true;
            return false;
        }

        _consecutive++;
        _lastAttempt = now;
        _inFlight = true;
        return true;
    }

    public void Complete() => _inFlight = false;

    /// <summary>Forgets the failure history — used when the layer is rebuilt for an unrelated
    /// reason (explorer restart, display change), which produces a fresh device anyway.</summary>
    public void Reset()
    {
        _consecutive = 0;
        _lastAttempt = DateTime.MinValue;
        GaveUp = false;
    }
}
