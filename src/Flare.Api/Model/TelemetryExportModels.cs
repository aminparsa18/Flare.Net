using MemoryPack;

namespace Flare.Api.Model;

/// <summary>Signals Flare can copy out. Profiles are not forwarded or archived (ADR-0155, ADR-0156).</summary>
public enum TelemetrySignal
{
    Logs,
    Traces,
    Metrics,
}

public enum ArchiveFileFormat
{
    Parquet,
    Ndjson,
}

/// <summary>
/// A managed OTLP forwarding target (ADR-0157). <see cref="Headers"/> values are masked in every response
/// (<see cref="Alerting.NotificationSecrets"/>); an update that sends a mask back keeps the stored value.
/// </summary>
[MemoryPackable]
public sealed partial record ForwardingTarget
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public bool Enabled { get; init; } = true;

    public required string Endpoint { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>Empty = all of logs, traces and metrics.</summary>
    public IReadOnlyList<TelemetrySignal> Signals { get; init; } = [];

    /// <summary>Empty = every service.</summary>
    public IReadOnlyList<string> Services { get; init; } = [];

    /// <summary>Empty = any request, authenticated or not.</summary>
    public IReadOnlyList<Guid> IngestKeyIds { get; init; } = [];

    public bool Gzip { get; init; } = true;

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

[MemoryPackable]
public sealed partial record ForwardingTargetListResponse
{
    public required IReadOnlyList<ForwardingTarget> Targets { get; init; }
}

[MemoryPackable]
public sealed partial record ForwardingTargetRequest
{
    public required string Name { get; init; }

    public bool? Enabled { get; init; }

    public required string Endpoint { get; init; }

    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    public IReadOnlyList<TelemetrySignal>? Signals { get; init; }

    public IReadOnlyList<string>? Services { get; init; }

    public IReadOnlyList<Guid>? IngestKeyIds { get; init; }

    public bool? Gzip { get; init; }

    /// <summary>An error message, or null when valid.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Name is required.";
        }

        if (!Uri.TryCreate(Endpoint?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return "Endpoint must be an absolute http(s) URL.";
        }

        if (Headers is not null && Headers.Keys.Any(string.IsNullOrWhiteSpace))
        {
            return "Header names must not be blank.";
        }

        return Services is not null && Services.Any(string.IsNullOrWhiteSpace) ? "Service names must not be blank." : null;
    }
}

/// <summary>Live state of one forwarding target, written by Flare.Ingest into Redis; managed and configuration-defined targets alike.</summary>
[MemoryPackable]
public sealed partial record ForwardingTargetStatus
{
    /// <summary>The managed target's id, or <c>config:&lt;name&gt;</c> for one defined in configuration.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    /// <summary><c>managed</c> or <c>config</c>.</summary>
    public required string Source { get; init; }

    /// <summary>Requests queued and not yet delivered.</summary>
    public required long Pending { get; init; }

    public required long Sent { get; init; }

    public required long Failed { get; init; }

    public DateTimeOffset? LastSuccessAt { get; init; }

    public string? LastError { get; init; }

    public DateTimeOffset? LastErrorAt { get; init; }
}

[MemoryPackable]
public sealed partial record ForwardingStatusResponse
{
    public required IReadOnlyList<ForwardingTargetStatus> Targets { get; init; }
}

/// <summary>
/// The managed S3 archive settings (ADR-0157). When none are saved the archive falls back to the worker's
/// <c>Archive</c> configuration. <see cref="AccessKey"/> and <see cref="SecretKey"/> are masked in responses.
/// </summary>
[MemoryPackable]
public sealed partial record ArchiveSettings
{
    public bool Enabled { get; init; }

    public string Endpoint { get; init; } = "";

    public string AccessKey { get; init; } = "";

    public string SecretKey { get; init; } = "";

    public string Prefix { get; init; } = "flare";

    public ArchiveFileFormat Format { get; init; } = ArchiveFileFormat.Parquet;

    /// <summary>Empty = all of logs, traces and metrics.</summary>
    public IReadOnlyList<TelemetrySignal> Signals { get; init; } = [];

    /// <summary>Whether settings are saved (true) or the archive still follows configuration (false).</summary>
    public bool Saved { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

[MemoryPackable]
public sealed partial record ArchiveSettingsRequest
{
    public bool? Enabled { get; init; }

    public required string Endpoint { get; init; }

    public string? AccessKey { get; init; }

    public string? SecretKey { get; init; }

    public string? Prefix { get; init; }

    public ArchiveFileFormat? Format { get; init; }

    public IReadOnlyList<TelemetrySignal>? Signals { get; init; }

    /// <summary>An error message, or null when valid. Credentials are checked after masked values are restored.</summary>
    public string? Validate()
    {
        if (!Uri.TryCreate(Endpoint?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return "Endpoint must be an absolute http(s) bucket URL.";
        }

        return null;
    }
}

[MemoryPackable]
public sealed partial record ArchiveTableStatus
{
    public required string Table { get; init; }

    /// <summary>Start of the most recent hour exported (or confirmed empty).</summary>
    public DateTimeOffset? LastExportedHour { get; init; }

    public DateTimeOffset? LastSuccessAt { get; init; }

    /// <summary>Rows in the last non-empty export.</summary>
    public long LastRows { get; init; }

    public string? LastError { get; init; }

    public DateTimeOffset? LastErrorAt { get; init; }
}

[MemoryPackable]
public sealed partial record ArchiveStatusResponse
{
    /// <summary>True when the worker has the archive on and recently reported in.</summary>
    public required bool Active { get; init; }

    /// <summary><c>settings</c>, <c>config</c>, or empty when the archive is off.</summary>
    public string Source { get; init; } = "";

    public DateTimeOffset? CheckedAt { get; init; }

    public required IReadOnlyList<ArchiveTableStatus> Tables { get; init; }
}
