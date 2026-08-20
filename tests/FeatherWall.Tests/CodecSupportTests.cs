using FeatherWall.Playback;
using Xunit;

namespace FeatherWall.Tests;

/// <summary>Startup preflight: naming the failure instead of showing a black desktop.
/// The trap this guards is inferring the codec from the file extension, which would produce
/// false errors on files that decode perfectly well.</summary>
public class CodecSupportTests
{
    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("clip.MP4")]
    [InlineData("clip.mov")]
    [InlineData("clip.webm")]
    [InlineData("clip.mkv")]
    [InlineData(@"C:\some path\with spaces\clip.wmv")]
    public void RecognisesVideoExtensions(string path) => Assert.True(CodecSupport.IsVideoExtension(path));

    [Theory]
    [InlineData("still.jpg")]
    [InlineData("still.png")]
    [InlineData("notes.txt")]
    [InlineData("noextension")]
    public void DoesNotTreatNonVideoAsVideo(string path) => Assert.False(CodecSupport.IsVideoExtension(path));

    [Theory]
    [InlineData("HEVC", "HEVC Video Extensions")]
    [InlineData("hevc", "HEVC Video Extensions")]
    [InlineData("hvc1", "HEVC Video Extensions")]
    [InlineData("VP9", "VP9 Video Extensions")]
    [InlineData("av01", "AV1 Video Extension")]
    public void MapsCodecsToTheStoreExtensionThatFixesThem(string codec, string expected) =>
        Assert.Equal(expected, CodecSupport.StoreExtensionFor(codec));

    [Theory]
    [InlineData("H264")]
    [InlineData("theora")]
    [InlineData("")]
    public void ReturnsNoExtensionWhereNoneWouldHelp(string codec) =>
        Assert.Null(CodecSupport.StoreExtensionFor(codec));

    [Fact]
    public void MissingCodecMessage_NamesTheCodecTheFileAndTheFix()
    {
        var message = CodecSupport.MissingCodecMessage(@"C:\wallpapers\aurora.mp4", "HEVC");

        Assert.Contains("aurora.mp4", message);
        Assert.Contains("HEVC", message);
        Assert.Contains("HEVC Video Extensions", message);
        Assert.DoesNotContain(@"C:\wallpapers", message); // filename only, not the user's paths
    }

    [Fact]
    public void MissingCodecMessage_SaysSoWhenNoStoreExtensionWouldHelp()
    {
        var message = CodecSupport.MissingCodecMessage("odd.mkv", "Theora");

        Assert.Contains("Theora", message);
        Assert.Contains("Re-encode", message);
        Assert.DoesNotContain("Microsoft Store", message);
    }

    [Fact]
    public void AnMp4CanHoldHevc_SoTheExtensionMustNotDecideTheCodec()
    {
        // Pinning the reasoning, not the code: .mp4 is a video extension, and that fact alone
        // must never be read as "this decodes". The codec comes from the track.
        Assert.True(CodecSupport.IsVideoExtension("could-be-hevc.mp4"));
        Assert.NotNull(CodecSupport.StoreExtensionFor("HEVC"));
    }
}
