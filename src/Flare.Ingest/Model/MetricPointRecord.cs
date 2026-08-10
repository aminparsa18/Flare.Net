using System.Text.Json.Serialization;

namespace Flare.Ingest.Model;

/// <summary>
/// Internal representation of a single OTLP metric data point, after mapping from OTLP.
/// </summary>
/// <remarks>
/// v1 covers three of OTLP's five point types - <see cref="GaugePointRecord"/>,
/// <see cref="SumPointRecord"/>, <see cref="HistogramPointRecord"/> - mirroring the
/// <c>metrics_gauge</c>/<c>metrics_sum</c>/<c>metrics_histogram</c> tables
/// (<c>db/clickhouse/0008_metrics.sql</c>) 1:1, keep them in sync.
/// ExponentialHistogram and Summary are deliberately not represented: no feature in this
/// roadmap slice consumes them, same "add it when a concrete need exists" precedent
/// <see cref="SpanRecord"/> used to omit Span Links. <see cref="OtlpMetricsMapper"/>
/// recognizes both point types on the wire and drops them rather than erroring.
///
/// The three concrete types share every field up to their own value shape, so unlike
/// <see cref="LogEvent"/>/<see cref="SpanRecord"/> (unrelated entities, no shared base)
/// this uses one abstract base for the ~12 fields that are genuinely identical across
/// all three - avoiding tripling that boilerplate for what's still one signal
/// (see Planning.md's v6 "Pipeline shape" decision, which made the same "one signal"
/// call for the ingest pipeline).
///
/// Polymorphic - <see cref="Pipeline.MetricEventJsonContext"/> is the matching
/// source-generated System.Text.Json contract that lets a single Redis stream/flush
/// worker carry all three point types together, discriminated by the
/// <see cref="JsonDerivedTypeAttribute"/> tags below.
///
/// Proto3 string fields can't distinguish "unset" from "explicitly empty string" on the
/// wire. <see cref="OtlpMetricsMapper"/> normalizes empty string to <see langword="null"/>
/// for every nullable string field here, same convention as <see cref="LogEvent"/>/
/// <see cref="SpanRecord"/>.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(GaugePointRecord), "gauge")]
[JsonDerivedType(typeof(SumPointRecord), "sum")]
[JsonDerivedType(typeof(HistogramPointRecord), "histogram")]
public abstract record MetricPointRecord
{
    /// <summary>The metric's name, e.g. <c>http.server.request.duration</c>.</summary>
    public required string MetricName { get; init; }

    public string? Description { get; init; }

    /// <summary>UCUM-style unit string, e.g. <c>ms</c>, <c>By</c>, <c>1</c>.</summary>
    public string? Unit { get; init; }

    /// <summary>Resource attribute "service.name", if present.</summary>
    public string? ServiceName { get; init; }

    public string? ResourceSchemaUrl { get; init; }

    public required IReadOnlyDictionary<string, string> ResourceAttributes { get; init; }

    public string? ScopeSchemaUrl { get; init; }

    public string? ScopeName { get; init; }

    public string? ScopeVersion { get; init; }

    public required IReadOnlyDictionary<string, string> ScopeAttributes { get; init; }

    /// <summary>The data point's own attributes - the set of key/value pairs that identify its timeseries.</summary>
    public required IReadOnlyDictionary<string, string> DataPointAttributes { get; init; }

    /// <summary>
    /// Optional per the OTLP spec (<c>StartTimeUnixNano</c>). When absent on the wire,
    /// <see cref="OtlpMetricsMapper"/> leaves this <see langword="null"/> and
    /// <see cref="Pipeline.ClickHouseMetricRowMapper"/> defaults the stored column to
    /// <see cref="Time"/> at insert time - same "coalesce null to a sibling required
    /// field, keep the column non-Nullable" convention as <c>LogEvent.ObservedTimestamp</c>.
    /// </summary>
    public DateTimeOffset? StartTime { get; init; }

    /// <summary>The moment this data point's aggregate value was captured. Required per the OTLP spec.</summary>
    public required DateTimeOffset Time { get; init; }
}

/// <summary>An OTLP Gauge data point - the "current value" at <see cref="MetricPointRecord.Time"/>, no aggregation temporality.</summary>
public sealed record GaugePointRecord : MetricPointRecord
{
    public required double Value { get; init; }
}

/// <summary>An OTLP Sum data point - a running total, either a monotonic counter or an up/down counter.</summary>
public sealed record SumPointRecord : MetricPointRecord
{
    public required double Value { get; init; }

    /// <summary>OTLP AggregationTemporality (0=unspecified, 1=delta, 2=cumulative).</summary>
    public required int AggregationTemporality { get; init; }

    public required bool IsMonotonic { get; init; }
}

/// <summary>An OTLP Histogram data point - bucketed distribution of a population of values.</summary>
public sealed record HistogramPointRecord : MetricPointRecord
{
    /// <summary>OTLP AggregationTemporality (0=unspecified, 1=delta, 2=cumulative).</summary>
    public required int AggregationTemporality { get; init; }

    public required ulong Count { get; init; }

    /// <summary>
    /// Optional per the OTLP spec; must be zero when <see cref="Count"/> is zero.
    /// <see langword="null"/> on the wire maps to 0 at row-mapping time, same convention
    /// as <see cref="MetricPointRecord.StartTime"/>.
    /// </summary>
    public double? Sum { get; init; }

    /// <summary>Bucket counts, one greater in length than <see cref="ExplicitBounds"/> (or both empty when no distribution is known).</summary>
    public required IReadOnlyList<ulong> BucketCounts { get; init; }

    /// <summary>Strictly increasing bucket upper bounds.</summary>
    public required IReadOnlyList<double> ExplicitBounds { get; init; }
}
