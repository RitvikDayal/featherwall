namespace FeatherWall.Widgets;

/// <summary>Whatever the system says is playing — any app with a media session, including a
/// browser tab. Reads the WinRT session manager already available through the referenced SDK
/// projection, so there is no new package and no network call.</summary>
public sealed partial class NowPlayingSource
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
}
