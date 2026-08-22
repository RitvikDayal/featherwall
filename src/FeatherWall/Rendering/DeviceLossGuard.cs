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
    private readonly Lock _sync = new();
    private int _consecutive;
    private DateTime _lastAttempt = DateTime.MinValue;
    private bool _inFlight;
    private bool _lostDuringRecovery;
    private bool _gaveUp;

    public DeviceLossGuard(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    public bool GaveUp { get { lock (_sync) { return _gaveUp; } } }

    /// <summary>True if the caller now owns a recovery and must call <see cref="Complete"/>.
    ///
    /// Every surface that presents reports the same lost device, and they present on their own
    /// threads, so this is entered concurrently by design. Unsynchronised, two callers could both
    /// pass the <c>_inFlight</c> check and rebuild the tree on top of each other, and the
    /// increment of <c>_consecutive</c> could be lost — which quietly raises the retry bound the
    /// class exists to enforce.</summary>
    public bool TryBegin()
    {
        lock (_sync)
        {
            if (_gaveUp) return false;

            // A loss raised while a rebuild is running is not noise to be dropped: the replacement
            // device can be lost as fast as it is made. Remember it so Complete can say so.
            if (_inFlight)
            {
                _lostDuringRecovery = true;
                return false;
            }

            var now = _now();
            if (now - _lastAttempt >= QuietPeriod) _consecutive = 0;

            if (_consecutive >= MaxConsecutiveAttempts)
            {
                _gaveUp = true;
                return false;
            }

            _consecutive++;
            _lastAttempt = now;
            _inFlight = true;
            return true;
        }
    }

    /// <summary>Ends the recovery. Returns true when a further device loss arrived while it was
    /// running, meaning the surface the rebuild produced is already dead and the caller must go
    /// again. The attempt bound still applies, so a genuinely dying adapter stops after
    /// <see cref="MaxConsecutiveAttempts"/> rather than looping here.</summary>
    public bool Complete()
    {
        lock (_sync)
        {
            _inFlight = false;
            bool again = _lostDuringRecovery;
            _lostDuringRecovery = false;
            return again;
        }
    }

    /// <summary>Forgets the failure history — used when the layer is rebuilt for an unrelated
    /// reason (explorer restart, display change), which produces a fresh device anyway.</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _consecutive = 0;
            _lastAttempt = DateTime.MinValue;
            _gaveUp = false;
            _lostDuringRecovery = false;
        }
    }
}
