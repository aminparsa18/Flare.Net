using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// A version of a service marked as deployed. See <c>docs-internal/adr/0182-release-tracking.md</c>.
/// </summary>
[MemoryPackable]
public sealed partial record Release
{
    public required string Id { get; init; }

    public required string Service { get; init; }

    /// <summary>The <c>service.version</c> resource attribute value this release is about.</summary>
    public required string Version { get; init; }

    public string Commit { get; init; } = "";

    /// <summary>Link to the commit, pull request or pipeline run.</summary>
    public string Url { get; init; } = "";

    public string Notes { get; init; } = "";

    public required DateTimeOffset DeployedAt { get; init; }

    public string CreatedBy { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Exception groups first seen under this version. Null unless the list was requested for one
    /// service (counting scans <c>spans</c>, so it is not done across every service).
    /// </summary>
    public long? NewErrorCount { get; init; }
}

[MemoryPackable]
public sealed partial record ReleaseListResponse
{
    public required IReadOnlyList<Release> Releases { get; init; }
}

/// <summary>Request body for <c>PUT /api/releases</c>: marks (or updates) one release.</summary>
[MemoryPackable]
public sealed partial record ReleaseRequest
{
    public const int MaxFieldLength = 500;
    public const int MaxNotesLength = 4000;

    public required string Service { get; init; }

    public required string Version { get; init; }

    public string Commit { get; init; } = "";

    public string Url { get; init; } = "";

    public string Notes { get; init; } = "";

    /// <summary>When it went out. Null = now.</summary>
    public DateTimeOffset? DeployedAt { get; init; }

    /// <summary>Returns an error message, or null when this request is valid.</summary>
    public string? Validate(DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(Service))
        {
            return "service is required.";
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            return "version is required.";
        }

        if (Service.Length > MaxFieldLength || Version.Length > MaxFieldLength || (Commit?.Length ?? 0) > MaxFieldLength || (Url?.Length ?? 0) > MaxFieldLength)
        {
            return $"service, version, commit and url must each be at most {MaxFieldLength} characters.";
        }

        if ((Notes?.Length ?? 0) > MaxNotesLength)
        {
            return $"notes must be at most {MaxNotesLength} characters.";
        }

        if (!string.IsNullOrEmpty(Url) && !(Uri.TryCreate(Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
        {
            return "url must be an absolute http(s) URL.";
        }

        if (DeployedAt is { } at && at > now.AddDays(1))
        {
            return "deployedAt must not be more than a day in the future.";
        }

        return null;
    }
}

/// <summary>An exception group whose earliest recorded occurrence was under the release's version.</summary>
[MemoryPackable]
public sealed partial record ReleaseNewError
{
    public required string ExceptionType { get; init; }

    public required string ExceptionMessage { get; init; }

    /// <summary>Occurrences since the group first appeared (in any version).</summary>
    public required long Occurrences { get; init; }

    public required long FirstSeenUnixMs { get; init; }

    public required long LastSeenUnixMs { get; init; }
}

[MemoryPackable]
public sealed partial record ReleaseErrorsResponse
{
    public required IReadOnlyList<ReleaseNewError> Errors { get; init; }

    /// <summary>How far before the deploy time earlier occurrences were looked for.</summary>
    public required int HistoryDays { get; init; }
}
