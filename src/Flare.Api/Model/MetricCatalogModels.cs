using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/metrics/catalog</c> - the Metrics catalog page: every metric
/// ingested in a recent window with its cardinality, see <see cref="Query.MetricCatalogQueryBuilder"/>.
/// </summary>
/// <remarks>
/// A relative <see cref="WindowMinutes"/>, not <c>From</c>/<c>To</c>, same shape as
/// <see cref="HostListRequest"/> - no <c>DateTimeOffset</c>/list member, so this carries
/// <c>[GenerateTypeScript]</c>.
/// </remarks>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record MetricCatalogRequest
{
    /// <summary>Lookback window; null/non-positive = <see cref="Query.MetricCatalogQueryBuilder.DefaultWindowMinutes"/>, clamped server-side.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Case-insensitive substring match against <c>MetricName</c>. Null/empty = no filter.</summary>
    public string? Search { get; init; }
}

/// <summary>
/// One metric's row in <see cref="MetricCatalogResponse"/>, aggregated across every service
/// emitting it - unlike <see cref="MetricNameInfo"/>, which is one row per (metric, service)
/// pair because a picker entry has to pin one service to chart.
/// </summary>
/// <remarks>
/// <see cref="LastReceivedUnixMs"/> is epoch milliseconds rather than a <c>DateTimeOffset</c>
/// so this type stays <c>[GenerateTypeScript]</c>-able, same trade-off as
/// <see cref="HostMetricsRequest.EndUnixMs"/>.
/// </remarks>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record MetricCatalogEntry
{
    public required string MetricName { get; init; }

    public required MetricPointType Type { get; init; }

    public string? Unit { get; init; }

    public string? Description { get; init; }

    public required long ServiceCount { get; init; }

    /// <summary>
    /// Active series in the window - distinct (<c>ServiceName</c>, <c>DataPointAttributes</c>)
    /// combinations, the same series identity <see cref="MetricQueryRequest"/> charts one line
    /// per. Approximate (ClickHouse <c>uniq</c>) past a few thousand.
    /// </summary>
    public required long SeriesCount { get; init; }

    /// <summary>Data points received in the window.</summary>
    public required long SampleCount { get; init; }

    public required long LastReceivedUnixMs { get; init; }
}

/// <summary>Response body for <c>POST /api/metrics/catalog</c>, ordered by <see cref="MetricCatalogEntry.SeriesCount"/> descending.</summary>
[MemoryPackable]
public sealed partial record MetricCatalogResponse
{
    public required int WindowMinutes { get; init; }

    public required IReadOnlyList<MetricCatalogEntry> Metrics { get; init; }

    /// <summary>
    /// True when more metrics matched than <see cref="Query.MetricCatalogQueryBuilder.MaxMetrics"/>.
    /// The cut keeps the highest-cardinality metrics, so the ones worth a look are never the
    /// ones dropped.
    /// </summary>
    public required bool Truncated { get; init; }
}

/// <summary>
/// Request body for <c>POST /api/metrics/catalog/detail</c> - one metric's drill-down. Carries
/// <see cref="Type"/> for the same reason <see cref="MetricQueryRequest"/> does: the caller
/// already has it from the catalog row.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record MetricCatalogDetailRequest
{
    public required string MetricName { get; init; }

    public required MetricPointType Type { get; init; }

    /// <summary>Same default/clamp as <see cref="MetricCatalogRequest.WindowMinutes"/>.</summary>
    public int? WindowMinutes { get; init; }
}

/// <summary>One service's share of a metric in <see cref="MetricCatalogDetailResponse.Services"/>.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record MetricCatalogServiceInfo
{
    public required string ServiceName { get; init; }

    public required long SeriesCount { get; init; }

    public required long SampleCount { get; init; }

    public required long LastReceivedUnixMs { get; init; }
}

/// <summary>
/// One <c>DataPointAttributes</c> key on the metric - the per-attribute view that shows
/// <i>which</i> key is driving a high <see cref="MetricCatalogEntry.SeriesCount"/> (e.g. a
/// user id recorded as an attribute).
/// </summary>
[MemoryPackable]
public sealed partial record MetricCatalogAttributeInfo
{
    public required string Key { get; init; }

    /// <summary>Approximate (ClickHouse <c>uniq</c>) past a few thousand, same as <see cref="MetricCatalogEntry.SeriesCount"/>.</summary>
    public required long DistinctValueCount { get; init; }

    /// <summary>Data points carrying this key - against the metric's total, shows whether the key is always set or only sometimes.</summary>
    public required long SampleCount { get; init; }

    /// <summary>The most frequent values (up to <see cref="Query.MetricCatalogQueryBuilder.SampleValueCount"/>), most frequent first.</summary>
    public required IReadOnlyList<string> SampleValues { get; init; }
}

/// <summary>
/// Another metric likely to be read alongside this one, with the evidence it was picked on -
/// see <see cref="Query.MetricRelatedRanker"/>.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record MetricCatalogRelatedMetric
{
    public required string MetricName { get; init; }

    public required MetricPointType Type { get; init; }

    /// <summary>The leading name segments both metrics share (e.g. <c>http.server</c>), null when none.</summary>
    public string? SharedNamePrefix { get; init; }

    public required int SharedAttributeKeyCount { get; init; }

    public required int SharedServiceCount { get; init; }
}

/// <summary>Response body for <c>POST /api/metrics/catalog/detail</c>.</summary>
[MemoryPackable]
public sealed partial record MetricCatalogDetailResponse
{
    public required string MetricName { get; init; }

    public required MetricPointType Type { get; init; }

    public string? Unit { get; init; }

    public string? Description { get; init; }

    public required int WindowMinutes { get; init; }

    /// <summary>Ordered by <see cref="MetricCatalogServiceInfo.SeriesCount"/> descending.</summary>
    public required IReadOnlyList<MetricCatalogServiceInfo> Services { get; init; }

    /// <summary>Ordered by <see cref="MetricCatalogAttributeInfo.DistinctValueCount"/> descending.</summary>
    public required IReadOnlyList<MetricCatalogAttributeInfo> Attributes { get; init; }

    /// <summary>Best match first, at most <see cref="Query.MetricCatalogQueryBuilder.MaxRelated"/>.</summary>
    public required IReadOnlyList<MetricCatalogRelatedMetric> Related { get; init; }
}
