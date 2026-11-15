using System.Text.Json;

namespace PrismOS.Config;

/// <summary>What a release check found.</summary>
public enum UpdateCheckOutcome
{
    /// <summary>The latest release is the running one.</summary>
    UpToDate,
    /// <summary>There is a release newer than the running one.</summary>
    Newer,
    /// <summary>The repository has published no releases at all. Not an error: a fork that has
    /// not cut a release yet is a perfectly ordinary state, and saying "no connection" about it
    /// would be a lie about why.</summary>
    NoReleases,
    /// <summary>The check could not be made: offline, rate limited, or something else went
    /// wrong. Never reported as "up to date", because that is a claim the check did not make.</summary>
    Failed,
}

/// <summary>What a release check found.</summary>
public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, string Tag = "", string Url = "");

/// <summary>Checks GitHub Releases for a newer PrismOS.
///
/// The public releases API needs no token and no account, which is the point: the app makes no
/// account, and it does not go online for anything else. A check that fails — offline, rate
/// limited, a repository with no releases — returns null and is reported as "could not check",
/// never as "up to date": telling someone they are up to date when the check never ran is a
/// lie that hides the one thing they asked about.</summary>
public static class UpdateCheck
{
    public const string RepositoryOwner = "YUouriii";
    public const string RepositoryName = "PrismOS";
    public const string RepositoryUrl = "https://github.com/YUouriii/PrismOS";

    /// <summary>Where this app came from, which the About page credits.</summary>
    public const string UpstreamUrl = "https://github.com/RitvikDayal/featherwall";

    private const string ReleasesApi =
        $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";

    /// <summary>The running version, without the fourth component the assembly carries.
    /// A release tag of "v0.3.2" and an assembly version of "0.3.2.0" have to compare as
    /// equal, and they do not as Version values.</summary>
    public static Version CurrentVersion()
    {
        var assembly = typeof(UpdateCheck).Assembly.GetName().Version;
        return assembly is null
            ? new Version(0, 0, 0)
            : new Version(assembly.Major, assembly.Minor, Math.Max(0, assembly.Build));
    }

    /// <summary>Asks GitHub for the latest release.</summary>
    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Add("User-Agent", $"PrismOS/{CurrentVersion()} (+{RepositoryUrl})");
            using var response = await client.GetAsync(ReleasesApi, cancellationToken).ConfigureAwait(false);

            // 404 is the API's way of saying the repository has no releases. That is a state, not
            // a failure, and it is worth telling apart from being offline.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new UpdateCheckResult(UpdateCheckOutcome.NoReleases);
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult(UpdateCheckOutcome.Failed);

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            string tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(tag)) return new UpdateCheckResult(UpdateCheckOutcome.Failed);

            string url = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() ?? RepositoryUrl : RepositoryUrl;
            bool newer = IsNewer(tag, CurrentVersion());
            return new UpdateCheckResult(newer ? UpdateCheckOutcome.Newer : UpdateCheckOutcome.UpToDate, tag, url);
        }
        catch (Exception ex)
        {
            Common.Log.Warn($"Update check failed: {ex.Message}");
            return new UpdateCheckResult(UpdateCheckOutcome.Failed);
        }
    }

    /// <summary>Reads a release tag such as "v0.4.0" or "0.4.0-beta" as a version. Null when it
    /// is not one.</summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string text = tag.Trim().TrimStart('v', 'V');
        // A pre-release suffix is not part of the number: "0.4.0-beta" is a version of 0.4.0.
        int dash = text.IndexOf('-');
        if (dash > 0) text = text[..dash];
        return Version.TryParse(text, out var version) ? version : null;
    }

    /// <summary>Whether a release tag is newer than the running version. An unreadable tag is
    /// never newer: guessing would nag someone about a version they are already past.</summary>
    public static bool IsNewer(string? tag, Version current)
    {
        var version = ParseVersion(tag);
        return version is not null && version > current;
    }
}
