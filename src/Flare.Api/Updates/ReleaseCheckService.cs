using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Flare.Api.Json;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Updates;

/// <summary>
/// Answers <c>GET /api/version</c>: this build's version and the latest Flare release on
/// GitHub (ADR-0068). The GitHub lookup is lazy and cached - it runs only when a request
/// arrives after <see cref="UpdateCheckOptions.Interval"/> has passed, one lookup at a time -
/// so an idle instance never calls out, and a dashboard left open doesn't either.
/// </summary>
/// <remarks>
/// <c>/releases/latest</c> is the primary source (it skips drafts and prereleases and carries
/// the release notes). When the repository has no published Release yet - the case for every
/// tag before v0.5.1 - GitHub answers 404 and this falls back to the highest stable
/// <c>vX.Y.Z</c> tag, which gives a version and link but no notes. A failed lookup is
/// remembered for <see cref="FailureRetryDelay"/> so an unreachable GitHub (air-gapped install
/// that didn't set <c>UpdateCheck__Enabled=false</c>, rate limit) isn't retried per request.
/// </remarks>
public sealed class ReleaseCheckService(
    IHttpClientFactory httpClientFactory,
    IOptions<UpdateCheckOptions> options,
    TimeProvider timeProvider,
    ILogger<ReleaseCheckService> logger)
{
    public const string HttpClientName = "flare-release-check";

    internal static readonly TimeSpan FailureRetryDelay = TimeSpan.FromHours(1);

    /// <summary>Release bodies are free-form Markdown; this bounds what a notice dialog has to render.</summary>
    internal const int MaxNotesLength = 20_000;

    /// <summary>This build's version string (the assembly's informational version, without <c>+commit</c> metadata).</summary>
    public static string CurrentVersion { get; } = ReadCurrentVersion();

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private LatestReleaseInfo? _latest;
    private DateTimeOffset _nextRefreshAt = DateTimeOffset.MinValue;

    public async Task<VersionInfoResponse> GetAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        // Nothing to compare a dev/edge build against, so don't spend a GitHub call on it -
        // this also keeps every local Aspire run from checking.
        if (!opts.Enabled || !SemanticVersion.TryParse(CurrentVersion, out var current))
        {
            return new VersionInfoResponse(CurrentVersion, false, null);
        }

        if (timeProvider.GetUtcNow() >= _nextRefreshAt)
        {
            await RefreshAsync(opts, cancellationToken);
        }

        var latest = _latest;
        var updateAvailable = latest is not null
            && SemanticVersion.TryParse(latest.Version, out var latestVersion)
            && latestVersion.CompareTo(current) > 0;
        return new VersionInfoResponse(CurrentVersion, updateAvailable, latest);
    }

    private async Task RefreshAsync(UpdateCheckOptions opts, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Another request may have refreshed while this one waited.
            if (timeProvider.GetUtcNow() < _nextRefreshAt)
            {
                return;
            }

            try
            {
                _latest = await FetchLatestAsync(opts.Repository, cancellationToken) ?? _latest;
                _nextRefreshAt = timeProvider.GetUtcNow() + opts.Interval;
            }
            // Deliberately broad: besides HttpRequestException/JsonException, the resilience
            // handler's timeout surfaces as Polly's TimeoutRejectedException. A best-effort
            // notice must never turn into a failed /api/version.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation(ex, "Update check against GitHub repository {Repository} failed; retrying in {Delay}.", opts.Repository, FailureRetryDelay);
                _nextRefreshAt = timeProvider.GetUtcNow() + FailureRetryDelay;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<LatestReleaseInfo?> FetchLatestAsync(string repository, CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);

        using (var response = await http.GetAsync($"repos/{repository}/releases/latest", cancellationToken))
        {
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                response.EnsureSuccessStatusCode();
                var release = await response.Content.ReadFromJsonAsync(GitHubJsonContext.Default.GitHubRelease, cancellationToken);
                if (release is not null && SemanticVersion.TryParse(release.TagName, out var version))
                {
                    return new LatestReleaseInfo(
                        version.ToString(),
                        release.TagName,
                        release.HtmlUrl ?? TagUrl(repository, release.TagName),
                        string.IsNullOrWhiteSpace(release.Name) ? null : release.Name,
                        release.PublishedAt,
                        Truncate(release.Body));
                }
            }
        }

        var tags = await http.GetFromJsonAsync($"repos/{repository}/tags?per_page=100", GitHubJsonContext.Default.GitHubTagArray, cancellationToken);
        var best = SemanticVersion.HighestStableTag((tags ?? []).Select(t => t.Name));
        return best is { } b
            ? new LatestReleaseInfo(b.Version.ToString(), b.Tag, TagUrl(repository, b.Tag), null, null, null)
            : null;
    }

    private static string TagUrl(string repository, string tag) =>
        $"https://github.com/{repository}/releases/tag/{Uri.EscapeDataString(tag)}";

    private static string? Truncate(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null
        : notes.Length <= MaxNotesLength ? notes
        : notes[..MaxNotesLength] + "\n\n…";

    private static string ReadCurrentVersion()
    {
        var informational = typeof(ReleaseCheckService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "dev";
        }

        // The SDK appends "+<commit sha>" when building from a git checkout.
        var plus = informational.IndexOf('+');
        return plus >= 0 ? informational[..plus] : informational;
    }
}

/// <summary>The fields of GitHub's release object the update check reads.</summary>
public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("body")] string? Body);

/// <summary>One entry of GitHub's list-tags response.</summary>
public sealed record GitHubTag([property: JsonPropertyName("name")] string Name);
