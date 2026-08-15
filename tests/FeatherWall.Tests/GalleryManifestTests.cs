using System.Text.RegularExpressions;
using FeatherWall.Gallery;

namespace FeatherWall.Tests;

public class GalleryManifestTests
{
    private static readonly GalleryManifest Manifest = new GalleryService().Manifest;

    [Fact]
    public void EmbeddedManifest_LoadsWithEntries() =>
        Assert.True(Manifest.Entries.Count >= 6);

    [Fact]
    public void AllEntries_HaveRequiredFields()
    {
        foreach (var entry in Manifest.Entries)
        {
            Assert.Matches("^[a-z0-9-]+$", entry.Id);
            Assert.False(string.IsNullOrWhiteSpace(entry.Title));
            Assert.True(entry.Type is "video" or "image");
            Assert.StartsWith("https://", entry.Url);
            Assert.StartsWith("https://", entry.SourcePage);
            Assert.False(string.IsNullOrWhiteSpace(entry.License));
            Assert.True(entry.Bytes > 0);
        }
    }

    [Fact]
    public void AllEntries_AreFromLegallyCleanSources()
    {
        foreach (var entry in Manifest.Entries)
        {
            var host = new Uri(entry.Url).Host;
            Assert.True(host is "upload.wikimedia.org" or "images-assets.nasa.gov",
                $"{entry.Id} points at unexpected host {host}");
            Assert.Contains(entry.License, new[] { "CC0", "Public Domain", "Public Domain (NASA)" });
        }
    }

    [Fact]
    public void Checksums_AreValidSha1Hex_WhenPresent()
    {
        foreach (var entry in Manifest.Entries.Where(e => e.Sha1 is not null))
            Assert.Matches(new Regex("^[0-9a-f]{40}$"), entry.Sha1!);
    }

    [Fact]
    public void Ids_AreUnique() =>
        Assert.Equal(Manifest.Entries.Count, Manifest.Entries.Select(e => e.Id).Distinct().Count());

    [Fact]
    public void SizeLabel_FormatsHumanReadably()
    {
        Assert.Equal("15 MB", new GalleryEntry { Bytes = 14_902_920 }.SizeLabel);
        Assert.Equal("4 KB", new GalleryEntry { Bytes = 4_000 }.SizeLabel);
        Assert.Equal("1.2 GB", new GalleryEntry { Bytes = 1_200_000_000 }.SizeLabel);
    }
}
