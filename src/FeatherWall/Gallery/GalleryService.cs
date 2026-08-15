using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FeatherWall.Common;

namespace FeatherWall.Gallery;

public sealed class GalleryEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = "video"; // "video" | "image"
    public string Url { get; set; } = "";
    public string SourcePage { get; set; } = "";
    public string Author { get; set; } = "";
    public string License { get; set; } = "";
    public string Attribution { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public long Bytes { get; set; }
    public string? Sha1 { get; set; }
    public bool MfNative { get; set; } = true;
    public string Description { get; set; } = "";

    public string SizeLabel => Bytes switch
    {
        >= 1_000_000_000 => $"{Bytes / 1_000_000_000.0:0.#} GB",
        >= 1_000_000 => $"{Bytes / 1_000_000.0:0} MB",
        _ => $"{Bytes / 1_000.0:0} KB",
    };
}

public sealed class GalleryManifest
{
    public List<GalleryEntry> Entries { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GalleryManifest))]
public sealed partial class GalleryJsonContext : JsonSerializerContext;

/// <summary>Built-in gallery: a curated manifest of public-domain / CC0 media (embedded in
/// the exe). Downloads happen only when the user picks an entry; checksums from the
/// manifest are verified when present.</summary>
public sealed class GalleryService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(10),
        DefaultRequestHeaders = { { "User-Agent", "FeatherWall/0.1 (+https://github.com/ritvikdayal/featherwall)" } },
    };

    public string GalleryDirectory { get; } = Path.Combine(Log.Directory, "gallery");

    public GalleryManifest Manifest { get; }

    public GalleryService()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FeatherWall.Gallery.gallery.json")
            ?? throw new InvalidOperationException("Embedded gallery manifest missing.");
        Manifest = JsonSerializer.Deserialize(stream, GalleryJsonContext.Default.GalleryManifest) ?? new GalleryManifest();
    }

    public string? LocalPathIfDownloaded(GalleryEntry entry)
    {
        var path = LocalPath(entry);
        return File.Exists(path) ? path : null;
    }

    private string LocalPath(GalleryEntry entry) =>
        Path.Combine(GalleryDirectory, entry.Id + Path.GetExtension(new Uri(entry.Url).AbsolutePath));

    private static readonly HashSet<string> InFlight = [];

    /// <summary>Downloads (if not cached) and returns the local file path.</summary>
    public async Task<string> EnsureDownloadedAsync(GalleryEntry entry, IProgress<double>? progress = null)
    {
        var path = LocalPath(entry);
        if (File.Exists(path)) return path;

        lock (InFlight)
        {
            if (!InFlight.Add(entry.Id))
                throw new InvalidOperationException("This wallpaper is already downloading.");
        }

        Directory.CreateDirectory(GalleryDirectory);
        var tmp = path + ".partial";
        Log.Info($"Gallery download: {entry.Id} from {entry.Url}");

        try
        {
            using (var response = await Http.GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var src = await response.Content.ReadAsStreamAsync();
                await using var dst = File.Create(tmp);
                var buffer = new byte[1 << 16];
                long total = 0;
                int read;
                while ((read = await src.ReadAsync(buffer)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read));
                    total += read;
                    if (entry.Bytes > 0) progress?.Report((double)total / entry.Bytes);
                }
            }

            if (!string.IsNullOrEmpty(entry.Sha1))
            {
                await using var check = File.OpenRead(tmp);
                var hash = Convert.ToHexStringLower(await SHA1.HashDataAsync(check));
                if (!hash.Equals(entry.Sha1, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Checksum mismatch for {entry.Id}: expected {entry.Sha1}, got {hash}.");
            }

            File.Move(tmp, path, overwrite: true);
            Log.Info($"Gallery download complete: {path}");
            return path;
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
        finally
        {
            lock (InFlight) InFlight.Remove(entry.Id);
        }
    }
}
