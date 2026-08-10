namespace Flare.Api.Model;

/// <summary>
/// Which of the three v1 point-type tables (<c>metrics_gauge</c>/<c>metrics_sum</c>/
/// <c>metrics_histogram</c>) a metric name lives in - see
/// <c>db/clickhouse/0008_metrics.sql</c>. Carried explicitly on <see cref="MetricQueryRequest"/>
/// rather than looked up server-side: the dashboard always gets a metric's type from a
/// prior <c>/api/metrics/names</c> response before querying it, so there's no ambiguity
/// to resolve and no extra round-trip to save the caller from stating what it already
/// knows.
/// </summary>
public enum MetricPointType
{
    Gauge,
    Sum,
    Histogram,
}

/// <summary>
/// One equality filter against <c>DataPointAttributes</c> - the metrics equivalent of
/// <c>SpanAttributeFilter</c>, minus the multi-bag complexity (spans filter across three
/// attribute bags; metrics v1 only ever filters the data point's own attributes - no
/// planned query needs resource/scope-attribute filtering here, see
/// <c>db/clickhouse/README.md</c>'s "No <c>ResourceAttributes</c> skip index" note).
/// </summary>
public sealed record MetricAttributeFilter
{
    public required string Key { get; init; }

    public required string Value { get; init; }
}

/// <summary>
/// Structured filter shared by <c>/api/metrics/names</c> and <c>/api/metrics/query</c> -
/// see <see cref="Query.MetricFilterSqlBuilder"/> for how this compiles to a parameterized
/// <c>WHERE</c> clause usable against any of the three point-type tables (they share
/// <c>Time</c>/<c>ServiceName</c>/<c>DataPointAttributes</c> columns).
/// </summary>
public sealed record MetricFilter
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    /// <summary>Exact <c>ServiceName</c> match, ANDed with every other filter. Empty/null = all services.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Equality filters over <c>DataPointAttributes</c>, ANDed together.</summary>
    public IReadOnlyList<MetricAttributeFilter>? Attributes { get; init; }
}

/// <summary>Request body for <c>POST /api/metrics/names</c> - metric discovery for a picker, not a full <see cref="MetricFilter"/> (no attribute filtering - see its remarks).</summary>
public sealed record MetricNamesRequest
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public IReadOnlyList<string>? Services { get; init; }
}

/// <summary>
/// One distinct (metric name, service) pair found in the requested scope, with enough
/// metadata to populate a picker entry. Not just distinct metric names: the same metric
/// name can legitimately be emitted by more than one service (see
/// <see cref="Query.MetricNamesQueryBuilder"/>'s remarks), and a picker entry has to
/// pick one to query - <see cref="ServiceName"/> is that pin, carried straight into
/// <see cref="MetricQueryRequest.Filter"/>.
/// </summary>
public sealed record MetricNameInfo
{
    public required string MetricName { get; init; }

    public required string ServiceName { get; init; }

    public required MetricPointType Type { get; init; }

    public string? Unit { get; init; }

    public string? Description { get; init; }
}

/// <summary>Response body for <c>POST /api/metrics/names</c>.</summary>
public sealed record MetricNamesResponse
{
    public required IReadOnlyList<MetricNameInfo> Metrics { get; init; }
}

/// <summary>
/// Request body for <c>POST /api/metrics/query</c> - the core, genuinely new endpoint:
/// a time-bucketed series for one metric, one attribute-set per <see cref="MetricSeries"/>.
/// </summary>
public sealed record MetricQueryRequest
{
    public required string MetricName { get; init; }

    public required MetricPointType Type { get; init; }

    public MetricFilter Filter { get; init; } = new();

    /// <summary>Bucket width, e.g. 60 for 1-minute buckets. Compiles to <c>toStartOfInterval(Time, INTERVAL n SECOND)</c>, same convention as <c>LogAggregateRequest.BucketWidthSeconds</c>.</summary>
    public required int BucketWidthSeconds { get; init; }
}

/// <summary>
/// One bucket's value for one series. Only the fields matching the series' <see cref="MetricQueryRequest.Type"/>
/// are populated - <see cref="Value"/> for Gauge/Sum, <see cref="Count"/>/<see cref="Sum"/>/
/// the percentiles for Histogram. Left as one shape rather than a per-type hierarchy
/// (unlike <c>Flare.Ingest</c>'s <see cref="Ingest.Model.MetricPointRecord"/>tree) since a
/// response caller already knows the type from the request it sent - a discriminated
/// response type would just be overhead here, not disambiguation.
/// </summary>
public sealed record MetricSeriesPoint
{
    public required DateTimeOffset BucketStart { get; init; }

    /// <summary>Gauge: average value in the bucket. Sum: <c>max(Value) - min(Value)</c> in the bucket - see <see cref="Query.MetricSeriesQueryBuilder"/>'s remarks for the known cumulative-reset caveat.</summary>
    public double? Value { get; init; }

    /// <summary>Histogram only: total observation count in the bucket.</summary>
    public long? Count { get; init; }

    /// <summary>Histogram only: total of observed values in the bucket.</summary>
    public double? Sum { get; init; }

    /// <summary>Histogram only: approximate median, via <see cref="Query.HistogramQuantileEstimator"/>. Null if the bucket has no data.</summary>
    public double? P50 { get; init; }

    public double? P90 { get; init; }

    public double? P99 { get; init; }
}

/// <summary>
/// One timeseries - one distinct (<see cref="ServiceName"/>, <c>DataPointAttributes</c>)
/// pair - within a <see cref="MetricQueryResponse"/>. <see cref="ServiceName"/> is
/// included in the series identity, not just the request-level filter: a query with no
/// (or a multi-value) service filter can still span more than one service, and without
/// this a series from two different services sharing the same (or no) data point
/// attributes would silently merge into one line - see
/// <see cref="Query.MetricSeriesQueryBuilder"/>'s remarks.
/// </summary>
public sealed record MetricSeries
{
    public required string ServiceName { get; init; }

    public required IReadOnlyDictionary<string, string> Attributes { get; init; }

    public required IReadOnlyList<MetricSeriesPoint> Points { get; init; }
}

/// <summary>Response body for <c>POST /api/metrics/query</c>.</summary>
public sealed record MetricQueryResponse
{
    public required IReadOnlyList<MetricSeries> Series { get; init; }
}
