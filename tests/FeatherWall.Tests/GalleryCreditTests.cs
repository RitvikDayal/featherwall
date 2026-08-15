using FeatherWall.Gallery;

namespace FeatherWall.Tests;

/// <summary>Attribution has to reach the screen, not just the manifest. These cover the two pieces
/// that make that possible: formatting a credit, and recovering which gallery entry a wallpaper
/// path came from.</summary>
public class GalleryCreditTests
{
    [Fact]
    public void CreditLine_PrefersTheSourcesOwnAttributionWording()
    {
        var entry = new GalleryEntry
        {
            Title = "The Blue Marble",
            Author = "NASA / Apollo 17 crew",
            Attribution = "Courtesy NASA",
            License = "Public Domain",
        };
        Assert.Equal("The Blue Marble — Courtesy NASA (Public Domain)", entry.CreditLine);
    }

    [Fact]
    public void CreditLine_FallsBackToAuthorWhenNoAttributionGiven()
    {
        var entry = new GalleryEntry { Title = "Marmolada", Author = "Marco Bonomo", License = "CC0" };
        Assert.Equal("Marmolada — Marco Bonomo (CC0)", entry.CreditLine);
    }

    [Fact]
    public void CreditLine_HandlesMissingPieces()
    {
        Assert.Equal("Untitled (CC0)", new GalleryEntry { Title = "Untitled", License = "CC0" }.CreditLine);
        Assert.Equal("Someone", new GalleryEntry { Author = "Someone" }.CreditLine);
        Assert.Equal("", new GalleryEntry().CreditLine);
    }

    [Fact]
    public void EveryShippedEntry_ProducesANonEmptyCredit()
    {
        // A CC BY entry could not be added lawfully without this holding.
        foreach (var entry in new GalleryService().Manifest.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.CreditLine), $"{entry.Id} has no displayable credit");
            Assert.Contains(entry.License, entry.CreditLine);
        }
    }

    [Fact]
    public void EntryForPath_RecoversTheEntryFromADownloadedFile()
    {
        var service = new GalleryService();
        var first = service.Manifest.Entries[0];
        var extension = Path.GetExtension(new Uri(first.Url).AbsolutePath);
        var path = Path.Combine(service.GalleryDirectory, first.Id + extension);

        Assert.Equal(first.Id, service.EntryForPath(path)?.Id);
    }

    [Fact]
    public void EntryForPath_ReturnsNullForTheUsersOwnFiles()
    {
        var service = new GalleryService();
        Assert.Null(service.EntryForPath(@"C:\Users\someone\Videos\my-own-clip.mp4"));
        Assert.Null(service.EntryForPath(null));
        Assert.Null(service.EntryForPath("   "));
    }

    [Fact]
    public void EntryForPath_IgnoresUnknownFilesInsideTheGalleryDirectory()
    {
        var service = new GalleryService();
        var stray = Path.Combine(service.GalleryDirectory, "not-a-manifest-entry.mp4");
        Assert.Null(service.EntryForPath(stray));
    }
}
