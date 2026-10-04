using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/services/version-comparison</c>: what changed in one service
/// between two <c>service.version</c> values. See <see cref="Query.VersionComparisonQueryBuilder"/>.
/// </summary>
[MemoryPackable]
public sealed partial record VersionComparisonRequest
{
    /// <summary>Exact <c>ServiceName</c>. Required.</summary>
    public string? Service { get; init; }

    /// <summary>The version to compare against. Null = the version first seen just before <see cref="CurrentVersion"/>.</summary>
    public string? BaselineVersion { get; init; }

    /// <summary>The version under inspection. Null = the version most recently first seen.</summary>
    public string? CurrentVersion { get; init; }

    /// <summary>How far back versions are discovered and compared; null/non-positive = default, clamped server-side.</summary>
    public int? LookbackHours { get; init; }
}

/// <summary>One <c>service.version</c> seen for the service within the lookback.</summary>
[MemoryPackable]
public sealed partial record ServiceVersionInfo
{
    public required string Version { get; init; }

    /// <summary>Earliest span seen carrying this version, Unix epoch milliseconds. Clamped to the lookback.</summary>
    public required long FirstSeenUnixMs { get; init; }

    public required long LastSeenUnixMs { get; init; }

    public required long SpanCount { get; init; }
}

/// <summary>Request volume, errors and latency of one endpoint under one version.</summary>
[MemoryPackable]
public sealed partial record VersionEndpointStats
{
    public required long Count { get; init; }

    public required long ErrorCount { get; init; }

    public required double P95DurationMs { get; init; }
}

/// <summary>One entry-span name (server/consumer spans) compared across the two versions. A side is null when that version never served it.</summary>
[MemoryPackable]
public sealed partial record VersionEndpointComparison
{
    public required string Endpoint { get; init; }

    public VersionEndpointStats? Baseline { get; init; }

    public VersionEndpointStats? Current { get; init; }
}

/// <summary>An exception type seen under the current version and never under the baseline.</summary>
[MemoryPackable]
public sealed partial record VersionNewException
{
    public required string ExceptionType { get; init; }

    public required long Count { get; init; }

    public required long FirstSeenUnixMs { get; init; }
}

/// <summary>An outbound target (external call or database) called under the current version and never under the baseline.</summary>
[MemoryPackable]
public sealed partial record VersionNewDependency
{
    /// <summary><c>External</c> or <c>Database</c>.</summary>
    public required string Kind { get; init; }

    public required string Target { get; init; }

    public required long CallCount { get; init; }

    public required long ErrorCount { get; init; }
}

/// <summary>A Drain log pattern (ADR-0007) seen under the current version and never under the baseline.</summary>
[MemoryPackable]
public sealed partial record VersionNewLogPattern
{
    public required string PatternId { get; init; }

    public required string Template { get; init; }

    public required long Count { get; init; }

    /// <summary>Highest OTel severity number among its occurrences.</summary>
    public required int MaxSeverityNumber { get; init; }

    public required long FirstSeenUnixMs { get; init; }
}

/// <summary>Response body for <c>POST /api/services/version-comparison</c>.</summary>
[MemoryPackable]
public sealed partial record VersionComparisonResponse
{
    public required int LookbackHours { get; init; }

    /// <summary>Newest first-seen first.</summary>
    public required IReadOnlyList<ServiceVersionInfo> Versions { get; init; }

    /// <summary>The resolved pair; null when the service has fewer than two versions (or the requested ones were never seen).</summary>
    public string? BaselineVersion { get; init; }

    public string? CurrentVersion { get; init; }

    /// <summary>Highest-volume first.</summary>
    public required IReadOnlyList<VersionEndpointComparison> Endpoints { get; init; }

    public required IReadOnlyList<VersionNewException> NewExceptions { get; init; }

    public required IReadOnlyList<VersionNewDependency> NewDependencies { get; init; }

    public required IReadOnlyList<VersionNewLogPattern> NewLogPatterns { get; init; }
}
