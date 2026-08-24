using FeatherWall.Common;
using Windows.Media.Control;

namespace FeatherWall.Widgets;

/// <summary>Whatever the system says is playing — any app with a media session, including a
/// browser tab. Reads the WinRT session manager already available through the referenced SDK
/// projection, so there is no new package and no network call.</summary>
public sealed partial class NowPlayingSource : IWidgetSource
{
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

        int cap = Math.Max(4, maxCharacters);
        return text.Length <= cap ? text : text[..(cap - 1)] + "…";
    }

    private readonly int _maxCharacters;
    private readonly Action<Action> _toMainThread;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _value;
    private bool _disposed;

    public string? Value => _value;
    public event Action? Changed;

    /// <summary><paramref name="toMainThread"/> is the engine's queue. WinRT raises these events
    /// on a pool thread, and everything downstream — the overlay, the composition host — belongs
    /// to the main thread.</summary>
    public NowPlayingSource(int maxCharacters, Action<Action> toMainThread)
    {
        _maxCharacters = maxCharacters;
        _toMainThread = toMainThread;
        _ = InitialiseAsync();
    }

    private async Task InitialiseAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => _toMainThread(AttachSession);
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
                next = Format(props?.Title, props?.Artist, playing, _maxCharacters);
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
            if (_disposed || next == _value) return;
            _value = next;
            Changed?.Invoke();
        });
    }

    public void Dispose()
    {
        _disposed = true;
        if (_session is null) return;
        _session.MediaPropertiesChanged -= OnMediaChanged;
        _session.PlaybackInfoChanged -= OnPlaybackChanged;
        _session = null;
    }
}
