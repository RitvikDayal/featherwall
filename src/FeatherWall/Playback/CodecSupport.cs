namespace FeatherWall.Playback;

/// <summary>Which video files this machine can actually decode.
///
/// FeatherWall ships no codecs and decodes through the OS media pipeline, so H.264 and MP4 work
/// everywhere while HEVC, VP9 and AV1 depend on Store extensions the user may not have. Without
/// this check an unsupported file produces a black desktop and no explanation, which is the
/// second-worst first impression the product can make.
///
/// The container extension does NOT identify the codec — an .mp4 can hold HEVC and a .mkv can
/// hold H.264 — so the extension is only used to decide whether a file is video at all. The
/// decision that matters is made against the track's real subtype.</summary>
public static class CodecSupport
{
    /// <summary>Extensions FeatherWall will attempt as video, from the README's list.</summary>
    public static readonly IReadOnlySet<string> VideoExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".avi", ".wmv", ".webm", ".mkv", ".m4v" };

    public static bool IsVideoExtension(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path));

    /// <summary>Store extension a missing decoder needs, keyed by the friendly codec name a
    /// CodecQuery subtype maps to. Null means "no extension will help".</summary>
    public static string? StoreExtensionFor(string codec) => codec.ToUpperInvariant() switch
    {
        "HEVC" or "H265" or "HVC1" or "HEV1" => "HEVC Video Extensions",
        "VP9" or "VP90" => "VP9 Video Extensions",
        "AV1" or "AV01" => "AV1 Video Extension",
        _ => null,
    };

    /// <summary>Human-readable failure for a codec this machine cannot decode. Names the codec
    /// and the fix rather than saying the file failed to load.</summary>
    public static string MissingCodecMessage(string path, string codec)
    {
        string extension = StoreExtensionFor(codec) is { } store
            ? $"Install \"{store}\" from the Microsoft Store and try again."
            : "Windows has no decoder for it, and no Store extension provides one. Re-encode the file as H.264 in an MP4 container.";

        return $"FeatherWall cannot decode this video.\n\n" +
               $"File: {Path.GetFileName(path)}\n" +
               $"Codec: {codec}\n\n" +
               extension;
    }
}
