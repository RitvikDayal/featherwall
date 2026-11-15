using PrismOS.Config;

namespace PrismOS.Tests;

/// <summary>The one-time move of a pre-rename FeatherWall install's state into the PrismOS
/// folders. The point of the migration is that an upgrader keeps their settings and — the part
/// that actually matters — their true original wallpaper.</summary>
public class LegacyMigrationTests
{
    private static (string Config, string Data) Dirs()
    {
        var root = Directory.CreateTempSubdirectory("prismos-migrate-").FullName;
        var config = Path.Combine(root, "legacy-config");
        var data = Path.Combine(root, "legacy-data");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(data);
        return (config, data);
    }

    private static (string Config, string Data) Targets()
    {
        var root = Directory.CreateTempSubdirectory("prismos-migrate-to-").FullName;
        return (Path.Combine(root, "config"), Path.Combine(root, "data"));
    }

    [Fact]
    public void ConfigIsCarriedOver()
    {
        var (legacy, legacyData) = Dirs();
        var (config, data) = Targets();
        File.WriteAllText(Path.Combine(legacy, "config.json"), """{ "fit": "Stretch" }""");

        Assert.True(LegacyMigration.Migrate(legacy, legacyData, config, data));
        Assert.Equal("""{ "fit": "Stretch" }""", File.ReadAllText(Path.Combine(config, "config.json")));
    }

    [Fact]
    public void OriginalWallpaperIsCarriedOver()
    {
        // Without this, quitting restores the live frame the old app set rather than the
        // wallpaper the user actually started with.
        var (legacy, legacyData) = Dirs();
        var (config, data) = Targets();
        File.WriteAllText(Path.Combine(legacyData, "original-wallpaper.txt"), @"C:\Users\me\real.png");

        Assert.True(LegacyMigration.Migrate(legacy, legacyData, config, data));
        Assert.Equal(@"C:\Users\me\real.png",
            File.ReadAllText(Path.Combine(data, "original-wallpaper.txt")).Trim());
    }

    [Fact]
    public void GalleryDownloadsAreCarriedOver()
    {
        var (legacy, legacyData) = Dirs();
        var (config, data) = Targets();
        Directory.CreateDirectory(Path.Combine(legacyData, "gallery"));
        File.WriteAllText(Path.Combine(legacyData, "gallery", "nebula.jpg"), "bytes");

        Assert.True(LegacyMigration.Migrate(legacy, legacyData, config, data));
        Assert.True(File.Exists(Path.Combine(data, "gallery", "nebula.jpg")));
    }

    [Fact]
    public void AnExistingPrismOSConfigIsNotOverwritten()
    {
        var (legacy, legacyData) = Dirs();
        var (config, data) = Targets();
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(legacy, "config.json"), """{ "fit": "Stretch" }""");
        File.WriteAllText(Path.Combine(config, "config.json"), """{ "fit": "Fill" }""");

        LegacyMigration.Migrate(legacy, legacyData, config, data);
        Assert.Equal("""{ "fit": "Fill" }""", File.ReadAllText(Path.Combine(config, "config.json")));
    }

    [Fact]
    public void TheOldFoldersAreLeftAlone()
    {
        // A FeatherWall still installed alongside keeps its own files: nothing is moved.
        var (legacy, legacyData) = Dirs();
        var (config, data) = Targets();
        File.WriteAllText(Path.Combine(legacy, "config.json"), "{}");

        LegacyMigration.Migrate(legacy, legacyData, config, data);
        Assert.True(File.Exists(Path.Combine(legacy, "config.json")));
    }

    [Fact]
    public void NothingToMigrate_IsNotAFailure()
    {
        var (legacy, legacyData) = Dirs();
        var (config, data) = Targets();

        Assert.False(LegacyMigration.Migrate(legacy, legacyData, config, data));
    }
}
