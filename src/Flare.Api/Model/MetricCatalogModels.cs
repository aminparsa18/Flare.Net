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

    /// <summary>True when <see cref="Unit"/>/<see cref="Description"/> come (at least partly) from an admin override - see <see cref="Query.MetricMetadataOverlay"/>.</summary>
    public required bool HasMetadataOverride { get; init; }
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

    /// <summary>The unit the instrumentation sent, before any override - what "Reset" restores.</summary>
    public string? EmittedUnit { get; init; }

    public string? EmittedDescription { get; init; }

    /// <summary>True when an admin override exists for this metric name.</summary>
    public required bool HasMetadataOverride { get; init; }

    /// <summary>The admin's "treat as counter" setting (ADR-0066) - only meaningful for a Gauge.</summary>
    public bool TreatAsCounter { get; init; }
}

/// <summary>
/// Request body for <c>PUT /api/metrics/metadata-overrides</c> (Admin only) - replaces the
/// metric's override. A null/blank member means "show the emitted value"; both null with
/// <see cref="TreatAsCounter"/> off is rejected, that's <c>DELETE</c>. See
/// docs-internal/adr/0065-metric-metadata-overrides.md and 0066-treat-gauge-as-counter.md.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record SetMetricMetadataOverrideRequest
{
    public required string MetricName { get; init; }

    public string? Unit { get; init; }

    public string? Description { get; init; }

    /// <summary>Chart the metric's Gauge points like a counter (ADR-0066). Enough on its own to make an override.</summary>
    public bool TreatAsCounter { get; init; }
}

/// <summary>
/// Request body for <c>POST /api/metrics/catalog/inspect</c> - the "inspect metric" view:
/// a few of the metric's series' raw samples, and how the Metrics explorer's time and space
/// aggregation reduce them. See <see cref="Query.MetricInspectReducer"/>.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record MetricCatalogInspectRequest
{
    public required string MetricName { get; init; }

    public required MetricPointType Type { get; init; }

    /// <summary>Lookback window; null/non-positive = <see cref="Query.MetricInspectQueryBuilder.DefaultWindowMinutes"/>, clamped server-side - kept short, this is a per-sample view.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Time-aggregation bucket width; null/non-positive = <see cref="Query.MetricInspectQueryBuilder.DefaultBucketWidthSeconds"/>, clamped server-side.</summary>
    public int? BucketWidthSeconds { get; init; }

    /// <summary>Only this service's series. Null/empty = every service.</summary>
    public string? ServiceName { get; init; }
}

/// <summary>
/// How one raw sample feeds its series' time aggregation - mirrors the per-row
/// classification in <see cref="Query.MetricSeriesQueryBuilder"/>'s Sum query (ADR-0035).
/// MemoryPack encodes this as its ordinal, so existing members keep their ordinals.
/// </summary>
public enum MetricInspectSampleKind
{
    /// <summary>Gauge: the value itself is averaged into its bucket.</summary>
    Level,

    /// <summary>Delta temporality: the value already is the increase since the previous point.</summary>
    Delta,

    /// <summary>Cumulative, first sample shown for the series: nothing earlier to diff against, contributes 0.</summary>
    First,

    /// <summary>Cumulative: the difference from the previous sample.</summary>
    Difference,

    /// <summary>Monotonic cumulative that went down (a restart): the value itself stands in for the increase.</summary>
    Reset,
}

/// <summary>
/// One raw data point. For Histogram/ExponentialHistogram, <see cref="Value"/> is the point's
/// observation <c>Count</c> - a histogram has no single scalar, and its count reduces exactly
/// like a monotonic counter.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record MetricInspectSample
{
    public required long TimeUnixMs { get; init; }

    public required double Value { get; init; }

    public required MetricInspectSampleKind Kind { get; init; }

    /// <summary>What this sample adds to its bucket: the value for a gauge, otherwise its increase per <see cref="Kind"/>.</summary>
    public required double Contribution { get; init; }
}

/// <summary>One time bucket's reduced value - per series (time aggregation) or across series (space aggregation).</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record MetricInspectBucket
{
    public required long BucketStartUnixMs { get; init; }

    /// <summary>Gauge: the average of the bucket's samples. Otherwise: the sum of their contributions (the bucket's increase).</summary>
    public required double Value { get; init; }

    public required int SampleCount { get; init; }
}

/// <summary>One series (a distinct <c>ServiceName</c> + <c>DataPointAttributes</c>) in <see cref="MetricCatalogInspectResponse"/>.</summary>
[MemoryPackable]
public sealed partial record MetricInspectSeries
{
    public required string ServiceName { get; init; }

    public required IReadOnlyDictionary<string, string> Attributes { get; init; }

    /// <summary>Oldest first, at most <see cref="Query.MetricInspectQueryBuilder.MaxSamplesPerSeries"/> - the most recent ones.</summary>
    public required IReadOnlyList<MetricInspectSample> Samples { get; init; }

    /// <summary>True when the series had more samples in the window than <see cref="Samples"/> holds; the oldest were dropped.</summary>
    public required bool SamplesTruncated { get; init; }

    /// <summary>Step 1, time aggregation: this series' samples reduced per bucket.</summary>
    public required IReadOnlyList<MetricInspectBucket> Buckets { get; init; }
}

/// <summary>Response body for <c>POST /api/metrics/catalog/inspect</c>.</summary>
[MemoryPackable]
public sealed partial record MetricCatalogInspectResponse
{
    public required string MetricName { get; init; }

    public required MetricPointType Type { get; init; }

    public required int WindowMinutes { get; init; }

    public required int BucketWidthSeconds { get; init; }

    /// <summary>Every series of the metric in the window (in scope of the request's service) - the response carries at most <see cref="Query.MetricInspectQueryBuilder.MaxSeries"/> of them.</summary>
    public required long TotalSeriesCount { get; init; }

    /// <summary>The series with the most samples in the window, most first.</summary>
    public required IReadOnlyList<MetricInspectSeries> Series { get; init; }

    /// <summary>
    /// Step 2, space aggregation: <see cref="Series"/> merged into one line per bucket the way a
    /// grouped chart merges series - summed increases, or for a gauge the average of every
    /// sample in the bucket across the series.
    /// </summary>
    public required IReadOnlyList<MetricInspectBucket> Merged { get; init; }
}
