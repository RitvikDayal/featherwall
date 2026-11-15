namespace PrismOS.Config;

/// <summary>One-time move of a pre-rename FeatherWall install's state into the PrismOS folders.
///
/// The rename changed three things a user would notice: the config directory
/// (<c>%APPDATA%\FeatherWall</c> → <c>%APPDATA%\PrismOS</c>), the data directory
/// (<c>%LOCALAPPDATA%\FeatherWall</c> → <c>%LOCALAPPDATA%\PrismOS</c>) and the log name. Without
/// this, an upgrader would open on a fresh-install config — Orbit, widget on — and, worse, the
/// saved <c>original-wallpaper.txt</c> would be missing, so quitting would restore the live frame
/// FeatherWall had set instead of the wallpaper the user actually started with.
///
/// Copy-only and idempotent: nothing is moved or deleted, a destination that already exists is
/// left alone, and a FeatherWall still installed alongside keeps its own files. The overload with
/// explicit paths is the testable one; <see cref="Migrate"/> just points it at the real folders.</summary>
public static class LegacyMigration
{
    public static bool Migrate() => Migrate(
        ConfigStore.LegacyDirectory, Common.Log.LegacyDirectory,
        ConfigStore.ConfigDirectory, Common.Log.Directory);

    /// <summary>Copies whatever is missing. Returns true when at least one thing was copied.</summary>
    public static bool Migrate(string legacyConfigDirectory, string legacyDataDirectory,
                               string configDirectory, string dataDirectory)
    {
        bool any = false;
        any |= CopyMissing(Path.Combine(legacyConfigDirectory, "config.json"),
                           Path.Combine(configDirectory, "config.json"));

        // The files the engine and the upgrade check read at start-up.
        string[] dataFiles = ["original-wallpaper.txt", "original-vd-wallpapers.tsv", "featherwall.log"];
        foreach (string name in dataFiles)
            any |= CopyMissing(Path.Combine(legacyDataDirectory, name), Path.Combine(dataDirectory, name));

        // Downloaded gallery media and captured static frames, so they are not fetched again.
        any |= CopyDirectoryMissing(Path.Combine(legacyDataDirectory, "gallery"), Path.Combine(dataDirectory, "gallery"));
        any |= CopyDirectoryMissing(Path.Combine(legacyDataDirectory, "static"), Path.Combine(dataDirectory, "static"));
        return any;
    }

    private static bool CopyMissing(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination)) return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
            return true;
        }
        catch { return false; }   // best effort: a failed copy must not stop start-up
    }

    /// <summary>Copies a flat directory's files, never merging into one that already exists —
    /// a PrismOS run has already written its own frames by the time this matters.</summary>
    private static bool CopyDirectoryMissing(string source, string destination)
    {
        if (!Directory.Exists(source) || Directory.Exists(destination)) return false;
        try
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.EnumerateFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            return true;
        }
        catch { return false; }
    }
}
