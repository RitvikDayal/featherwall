using FeatherWall.Common;
using Windows.Foundation;
using Windows.Media.Control;

namespace FeatherWall.Widgets;

/// <summary>Whatever the system says is playing — any app with a media session, including a
/// browser tab. Reads the WinRT session manager already available through the referenced SDK
/// projection, so there is no new package and no network call.</summary>
/// <summary>What is playing, kept in parts. The record draws the title and the artist at different
/// sizes and weights, so a single joined string is the wrong shape for it.
///
/// TrackId is what the album-art cache keys on. It must change when the track changes and hold
/// still across a progress or playback tick, or the art is either stale or refetched constantly.</summary>
public readonly record struct NowPlayingReading(string? Title, string? Artist, bool IsPlaying, string TrackId);

public sealed partial class NowPlayingSource : IWidgetSource
{
    /// <summary>Pure, so the parts and the cache key are testable without a live session.
    ///
    /// Deliberately does not include IsPlaying in TrackId: pausing dims the record, and throwing
    /// the artwork away and refetching it on every pause would be a visible stutter for nothing.</summary>
    public static NowPlayingReading Read(string? title, string? artist, bool isPlaying)
    {
        string? cleanTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        string? cleanArtist = string.IsNullOrWhiteSpace(artist) ? null : artist.Trim();

        return new NowPlayingReading(
            isPlaying ? cleanTitle : null,
            isPlaying ? cleanArtist : null,
            isPlaying,
            $"{cleanTitle}␟{cleanArtist}");
    }

    /// <summary>Pure, so the display rules are tested without a live session.
    ///
    /// A paused track is not "now playing" and shows nothing: the line is meant to say what you
    /// are listening to, and a stale title left on the wallpaper after the music stops is worse
    /// than an empty space.</summary>
    public static string? Format(string? title, string? artist, bool isPlaying, int maxCharacters)
    {
        if (!isPlaying || string.IsNullOrWhiteSpace(title)) return null;

        string text = string.IsNullOrWhiteSpace(artist)
            ? $"♪ {title.Trim()}"
            : $"♪ {title.Trim()} — {artist.Trim()}";

        // The budget is honoured exactly, including the small values the settings panel does not
        // offer but the JSON accepts. Raising it to a floor of four meant maxCharacters: 1
        // produced four characters — a limit that quietly did not apply is worse than none.
        if (maxCharacters <= 0) return null;
        if (text.Length <= maxCharacters) return text;
        return maxCharacters == 1 ? "…" : text[..(maxCharacters - 1)] + "…";
    }

    private readonly Func<int> _maxCharacters;
    private readonly Action<Action> _toMainThread;
    private TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs>? _onSessionChanged;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _value;
    private int _generation;
    private bool _disposed;

    /// <summary>Serialises the end of InitialiseAsync against Dispose. RequestAsync resumes on a
    /// pool thread, so checking _disposed and then subscribing is two steps a main-thread Dispose
    /// can run between — leaving a handler attached to a source nobody will unsubscribe.</summary>
    private readonly Lock _lifetime = new();

    public string? Value => _value;
    public event Action? Changed;

    /// <summary><paramref name="toMainThread"/> is the engine's queue. WinRT raises these events
    /// on a pool thread, and everything downstream — the overlay, the composition host — belongs
    /// to the main thread.
    ///
    /// <paramref name="maxCharacters"/> is read on each format rather than captured, so this
    /// source outlives a settings change and the media session is not rebuilt for one.</summary>
    public NowPlayingSource(Func<int> maxCharacters, Action<Action> toMainThread)
    {
        _maxCharacters = maxCharacters;
        _toMainThread = toMainThread;
        _ = InitialiseAsync();
    }

    /// <summary>Re-reads the session. Used when the character budget changes — the value needs
    /// re-formatting without the subscription being torn down and rebuilt.</summary>
    public void Refresh() => _ = RefreshAsync();

    private async Task InitialiseAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

            // RequestAsync can outlive this source — the engine disposes and rebuilds widgets on
            // any display change. Subscribing after that would leave a handler on a manager
            // nobody unsubscribes from, and queue an AttachSession onto a disposed source.
            lock (_lifetime)
            {
                if (_disposed) return;

                _manager = manager;
                _onSessionChanged = (_, _) => _toMainThread(AttachSession);
                _manager.CurrentSessionChanged += _onSessionChanged;
            }
            _toMainThread(AttachSession);
        }
        catch (Exception ex)
        {
            // No media session support is not an error worth stopping for — the line just never
            // appears, which is the same as nothing playing.
            Log.Warn($"Now-playing unavailable: {ex.Message}");
        }
    }

    private void AttachSession()
    {
        if (_disposed) return;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaChanged;
            _session.PlaybackInfoChanged -= OnPlaybackChanged;
        }

        _session = _manager?.GetCurrentSession();
        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnMediaChanged;
            _session.PlaybackInfoChanged += OnPlaybackChanged;
        }

        _ = RefreshAsync();
    }

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ = RefreshAsync();
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        // Each event starts its own task, so two reads can be in flight at once and the older one
        // can finish last. Without this an outdated title overwrites the current one, and
        // comparing against _value does not catch it because the values genuinely differ.
        int generation = Interlocked.Increment(ref _generation);

        string? next = null;
        try
        {
            var session = _session;
            if (session is not null)
            {
                var info = session.GetPlaybackInfo();
                bool playing = info?.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                var props = await session.TryGetMediaPropertiesAsync();
                next = Format(props?.Title, props?.Artist, playing, _maxCharacters());
            }
        }
        catch (Exception ex)
        {
            // A session can vanish between the null check and the read. Show nothing rather
            // than leaving the last track on the wallpaper.
            Log.Warn($"Now-playing read failed: {ex.Message}");
        }

        _toMainThread(() =>
        {
            if (_disposed || generation != Volatile.Read(ref _generation) || next == _value) return;
            _value = next;
            Changed?.Invoke();
        });
    }

    public void Dispose()
    {
        lock (_lifetime)
        {
            _disposed = true;

            if (_manager is not null && _onSessionChanged is not null)
            {
                _manager.CurrentSessionChanged -= _onSessionChanged;
                _onSessionChanged = null;
            }
            _manager = null;
        }

        if (_session is null) return;
        _session.MediaPropertiesChanged -= OnMediaChanged;
        _session.PlaybackInfoChanged -= OnPlaybackChanged;
        _session = null;
    }
}
