using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/hosts</c> - the Hosts page's inventory table: one row per
/// OTel <c>host.name</c> resource attribute found on ingested <c>hostmetrics</c>-receiver
/// metrics (<c>system.*</c>) - see <see cref="Query.HostInventoryQueryBuilder"/>.
/// </summary>
/// <remarks>
/// A relative <see cref="WindowMinutes"/>, not <c>From</c>/<c>To</c>, same shape as
/// <see cref="ServiceOverviewRequest"/> - and like it, no <c>DateTimeOffset</c>/list member,
/// so this carries <c>[GenerateTypeScript]</c> (see <c>Flare.Api.csproj</c>'s MemoryPack
/// TypeScript codegen comment). <see cref="OsType"/> is a single value rather than a list
/// for the same reason.
/// </remarks>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record HostListRequest
{
    /// <summary>Lookback window; null/non-positive = <see cref="Query.HostInventoryQueryBuilder.DefaultWindowMinutes"/>, clamped server-side.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Case-insensitive substring match against <c>host.name</c>. Null/empty = no filter.</summary>
    public string? Search { get; init; }

    /// <summary>Exact match against the <c>os.type</c> resource attribute (e.g. <c>linux</c>). Null/empty = all.</summary>
    public string? OsType { get; init; }
}

/// <summary>
/// One host's row in <see cref="HostListResponse"/>. Every utilization figure is a
/// percentage (0-100) averaged over the request window, null when the host sent no data for
/// that metric in the window (e.g. the <c>filesystem</c> scraper isn't enabled) - never
/// silently 0. See <see cref="Query.HostInventoryQueryBuilder"/>'s remarks for exactly
/// which metric each column is derived from.
/// </summary>
[MemoryPackable]
public sealed partial record HostSummary
{
    public required string HostName { get; init; }

    /// <summary>The <c>os.type</c> resource attribute, null when the sender didn't set it.</summary>
    public string? OsType { get; init; }

    public double? CpuPercent { get; init; }

    public double? MemoryPercent { get; init; }

    public double? DiskPercent { get; init; }

    /// <summary><c>system.cpu.load_average.15m</c>, averaged over the window - a raw load figure, not a percentage.</summary>
    public double? LoadAverage15m { get; init; }

    /// <summary>Timestamp of the newest <c>system.*</c> data point this host sent within the window.</summary>
    public required DateTimeOffset LastSeen { get; init; }
}

/// <summary>Response body for <c>POST /api/hosts</c>, sorted by <see cref="HostSummary.HostName"/>.</summary>
[MemoryPackable]
public sealed partial record HostListResponse
{
    public required int WindowMinutes { get; init; }

    public required IReadOnlyList<HostSummary> Hosts { get; init; }

    /// <summary>True when more hosts matched than <see cref="Query.HostInventoryQueryBuilder.MaxHosts"/> - the list was cut, narrow the filter.</summary>
    public required bool Truncated { get; init; }
}

/// <summary>
/// Request body for <c>POST /api/hosts/metrics</c> - one host's drill-down charts. Same
/// generatable-shape reasoning as <see cref="HostListRequest"/>.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record HostMetricsRequest
{
    public required string HostName { get; init; }

    /// <summary>Same default/clamp as <see cref="HostListRequest.WindowMinutes"/>. The bucket width is derived from it server-side (<see cref="Query.HostInventoryQueryBuilder.BucketWidthSecondsFor"/>).</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>
    /// Where the window ends, as Unix epoch milliseconds; null = now. Lets the log event
    /// detail view chart a window around a past log's timestamp rather than only "the last
    /// N minutes". Epoch milliseconds rather than a <c>DateTimeOffset</c> so this type stays
    /// <c>[GenerateTypeScript]</c>-able - see <see cref="Query.HostInventoryQueryBuilder.ResolveWindowEnd"/>.
    /// </summary>
    public long? EndUnixMs { get; init; }
}

/// <summary>One time bucket of <see cref="HostMetricsResponse"/> - same per-column meaning and null semantics as <see cref="HostSummary"/>, scoped to the bucket instead of the whole window.</summary>
[MemoryPackable]
public sealed partial record HostMetricsPoint
{
    public required DateTimeOffset BucketStart { get; init; }

    public double? CpuPercent { get; init; }

    public double? MemoryPercent { get; init; }

    public double? DiskPercent { get; init; }

    public double? LoadAverage15m { get; init; }
}

/// <summary>Response body for <c>POST /api/hosts/metrics</c>, <see cref="Points"/> ascending by bucket. Buckets with no data in any column are absent, not zero-filled.</summary>
[MemoryPackable]
public sealed partial record HostMetricsResponse
{
    public required string HostName { get; init; }

    public required int WindowMinutes { get; init; }

    public required int BucketWidthSeconds { get; init; }

    public required IReadOnlyList<HostMetricsPoint> Points { get; init; }
}
