using System.Drawing;
using FeatherWall.Config;
using FeatherWall.Widgets;
using Xunit;

namespace FeatherWall.Tests;

/// <summary>Layout of the now-playing block. Pure, so none of it needs a GPU — what it looks like
/// is checked by rendering it.</summary>
public class NowPlayingLayoutTests
{
    private static DiscConfig Config() => new() { Size = 100 };
    private static NowPlayingReading Playing(string? title = "Blue in Green", string? artist = "Miles Davis") =>
        NowPlayingSource.Read(title, artist, isPlaying: true);

    [Fact]
    public void NothingPlaying_MeasuresEmpty()
    {
        var m = NowPlayingRenderer.Measure(Config(), NowPlayingSource.Read(null, null, false));
        Assert.Equal(Size.Empty, m.Total);
        Assert.Equal(Rectangle.Empty, m.DiscBox);
    }

    [Fact]
    public void Playing_PutsTheDiscBeforeTheText()
    {
        var m = NowPlayingRenderer.Measure(Config(), Playing());
        Assert.True(m.DiscBox.Right <= m.TitleBox.Left, $"disc {m.DiscBox} overlaps title {m.TitleBox}");
    }

    [Fact]
    public void ArtistSitsBelowTheTitle()
    {
        var m = NowPlayingRenderer.Measure(Config(), Playing());
        Assert.True(m.ArtistBox.Top >= m.TitleBox.Bottom, "artist must not overlap the title");
        Assert.Equal(m.TitleBox.X, m.ArtistBox.X);
    }

    [Fact]
    public void NoArtist_LeavesNoRoomForOne()
    {
        var m = NowPlayingRenderer.Measure(Config(), Playing(artist: null));
        Assert.Equal(Rectangle.Empty, m.ArtistBox);
    }

    [Fact]
    public void TotalContainsEveryBox()
    {
        // A box outside Total is a box clipped off the surface.
        var m = NowPlayingRenderer.Measure(Config(), Playing());
        var total = new Rectangle(0, 0, m.Total.Width, m.Total.Height);
        Assert.True(total.Contains(m.DiscBox), $"disc {m.DiscBox} outside {total}");
        Assert.True(total.Contains(m.TitleBox), $"title {m.TitleBox} outside {total}");
        Assert.True(total.Contains(m.ArtistBox), $"artist {m.ArtistBox} outside {total}");
    }

    [Fact]
    public void DiscOff_StillLaysOutTheText()
    {
        var m = NowPlayingRenderer.Measure(new DiscConfig { Enabled = false }, Playing());
        Assert.Equal(Rectangle.Empty, m.DiscBox);
        Assert.False(m.TitleBox.IsEmpty);
        Assert.True(m.Total.Width > 0);
    }

    [Fact]
    public void Scale_GrowsTheWholeBlock()
    {
        var small = NowPlayingRenderer.Measure(Config(), Playing(), 1f);
        var large = NowPlayingRenderer.Measure(Config(), Playing(), 1.5f);
        Assert.True(large.Total.Width > small.Total.Width);
        Assert.True(large.DiscBox.Width > small.DiscBox.Width);
    }

    [Fact]
    public void LetterSpacing_WidensTheArtistLine()
    {
        // GDI+ has no tracking, so it is drawn glyph by glyph — and must be measured that way too,
        // or the box is narrower than the text painted into it.
        var tight = NowPlayingRenderer.Measure(new DiscConfig { Size = 100, ArtistLetterSpacing = 0f }, Playing());
        var spaced = NowPlayingRenderer.Measure(new DiscConfig { Size = 100, ArtistLetterSpacing = 4f }, Playing());
        Assert.True(spaced.ArtistBox.Width > tight.ArtistBox.Width);
    }
}

/// <summary>Cases found by running it: local media often carries no metadata at all.</summary>
public class NowPlayingEdgeTests
{
    [Fact]
    public void PlayingWithNoTitle_StillShowsTheRecord()
    {
        // A local file with no tags reports playing with an empty title. Something IS playing, so
        // the record appears — with artwork or a flat disc — and simply has no words beside it.
        var reading = NowPlayingSource.Read(null, null, isPlaying: true);
        var m = NowPlayingRenderer.Measure(new DiscConfig { Size = 100 }, reading);
        Assert.False(m.DiscBox.IsEmpty);
        Assert.Equal(Rectangle.Empty, m.TitleBox);
    }

    [Fact]
    public void NothingAtAll_ShowsNothing()
    {
        var reading = NowPlayingSource.Read(null, null, isPlaying: false);
        Assert.Equal(Size.Empty, NowPlayingRenderer.Measure(new DiscConfig(), reading).Total);
    }

    [Fact]
    public void Paused_KeepsTheWholeBlockOnScreen()
    {
        var reading = NowPlayingSource.Read("Blue in Green", "Miles Davis", isPlaying: false);
        var m = NowPlayingRenderer.Measure(new DiscConfig { Size = 100 }, reading);
        Assert.False(m.DiscBox.IsEmpty);
        Assert.False(m.TitleBox.IsEmpty);
    }
}
