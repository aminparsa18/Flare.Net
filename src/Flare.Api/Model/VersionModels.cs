namespace Flare.Api.Model;

/// <summary>
/// <c>GET /api/version</c>'s response - the running build's version plus, when the update
/// check is on and has succeeded, the latest GitHub release (ADR-0068).
/// </summary>
/// <param name="Current">This build's version, or <c>dev</c> for a build that isn't a tagged release (local, <c>edge</c> image).</param>
/// <param name="UpdateAvailable">True only when both versions parse and <see cref="Latest"/> is newer.</param>
/// <param name="Latest">Null when the check is off, skipped (a <c>dev</c> build) or hasn't succeeded yet.</param>
public sealed record VersionInfoResponse(string Current, bool UpdateAvailable, LatestReleaseInfo? Latest);

/// <param name="Version">Normalized, without the tag's <c>v</c> prefix.</param>
/// <param name="Url">The release page on GitHub.</param>
/// <param name="Name">The release title; null when only a tag exists (no published Release).</param>
/// <param name="Notes">The release body (Markdown, truncated); null when only a tag exists.</param>
public sealed record LatestReleaseInfo(
    string Version,
    string Tag,
    string Url,
    string? Name,
    DateTimeOffset? PublishedAt,
    string? Notes);
