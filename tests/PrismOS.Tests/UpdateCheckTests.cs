using PrismOS.Config;

namespace PrismOS.Tests;

/// <summary>The release check. The part that can be wrong quietly is the comparison: a check that
/// says "up to date" when the version was never read hides the one thing the person asked.</summary>
public class UpdateCheckTests
{
    [Theory]
    [InlineData("v0.4.0", 0, 4, 0)]
    [InlineData("0.4.0", 0, 4, 0)]
    [InlineData("V1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3-beta", 1, 2, 3)]
    public void ParseVersion_ReadsATag(string tag, int major, int minor, int build)
    {
        var version = UpdateCheck.ParseVersion(tag);

        Assert.NotNull(version);
        Assert.Equal(major, version!.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(build, version.Build);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData(null)]
    public void ParseVersion_RefusesWhatIsNotAVersion(string? tag)
    {
        Assert.Null(UpdateCheck.ParseVersion(tag));
    }

    [Fact]
    public void IsNewer_SaysYesOnlyForANewerTag()
    {
        var current = new Version(1, 0, 0);

        Assert.True(UpdateCheck.IsNewer("v1.0.1", current));
        Assert.True(UpdateCheck.IsNewer("v2.0.0", current));
        Assert.False(UpdateCheck.IsNewer("v1.0.0", current));
        Assert.False(UpdateCheck.IsNewer("v0.9.9", current));
    }

    [Fact]
    public void IsNewer_NeverGuesses()
    {
        // An unreadable tag is not newer. Guessing would nag somebody about a version they are
        // already past, or hide one they are not.
        Assert.False(UpdateCheck.IsNewer("not-a-version", new Version(1, 0, 0)));
        Assert.False(UpdateCheck.IsNewer(null, new Version(1, 0, 0)));
    }

    [Fact]
    public void TheCurrentVersionHasNoFourthComponent()
    {
        // A tag of "v0.3.2" and an assembly version of "0.3.2.0" have to compare as equal, and
        // as Version values they do not.
        var current = UpdateCheck.CurrentVersion();

        Assert.False(current.Build < 0);
        Assert.False(UpdateCheck.IsNewer($"v{current}", current));
    }

    [Fact]
    public void TheRepositoryIsThisFork()
    {
        Assert.Equal("YUouriii", UpdateCheck.RepositoryOwner);
        Assert.Equal("PrismOS", UpdateCheck.RepositoryName);
        Assert.Equal("https://github.com/YUouriii/PrismOS", UpdateCheck.RepositoryUrl);
    }

    [Fact]
    public void TheUpstreamIsCredited()
    {
        Assert.Equal("https://github.com/RitvikDayal/featherwall", UpdateCheck.UpstreamUrl);
    }
}
