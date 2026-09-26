using System.Text.Json.Serialization;
using MemoryPack;

namespace Flare.Ingest.Model;

/// <summary>
/// Internal representation of a single OTLP metric data point, after mapping from OTLP.
/// </summary>
/// <remarks>
/// Covers four of OTLP's five point types - <see cref="GaugePointRecord"/>,
/// <see cref="SumPointRecord"/>, <see cref="HistogramPointRecord"/>,
/// <see cref="ExponentialHistogramPointRecord"/> - mirroring the
/// <c>metrics_gauge</c>/<c>metrics_sum</c>/<c>metrics_histogram</c> tables
/// (<c>db/clickhouse/0008_metrics.sql</c>) and <c>metrics_exponential_histogram</c>
/// (<c>0032_metrics_exponential_histogram.sql</c>) 1:1, keep them in sync. Summary (legacy
/// Prometheus-client-style precomputed quantiles) is deliberately not represented:
/// <see cref="OtlpMetricsMapper"/> recognizes it on the wire and drops it rather than
/// erroring.
///
/// The concrete types share every field up to their own value shape, so unlike
/// <see cref="LogEvent"/>/<see cref="SpanRecord"/> (unrelated entities, no shared base)
/// this uses one abstract base for the ~12 fields that are genuinely identical across
/// all of them - avoiding repeating that boilerplate for what's still one signal
/// (see Planning.md's v6 "Pipeline shape" decision, which made the same "one signal"
/// call for the ingest pipeline).
///
/// Polymorphic - <see cref="Pipeline.MetricEventJsonContext"/> is the matching
/// source-generated System.Text.Json contract that lets a single Redis stream/flush
/// worker carry every point type together, discriminated by the
/// <see cref="JsonDerivedTypeAttribute"/> tags below. <see cref="Pipeline.MetricFlushWorker"/>
/// only ever reads that contract back for entries buffered before ADR-0017's MemoryPack
/// migration - the <see cref="MemoryPackUnionAttribute"/> tags below are the wire format
/// for every entry this process itself writes now, same discriminated-union shape
/// mirrored into MemoryPack's own union mechanism rather than System.Text.Json's.
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
[JsonDerivedType(typeof(ExponentialHistogramPointRecord), "exponentialHistogram")]
[MemoryPackable]
[MemoryPackUnion(0, typeof(GaugePointRecord))]
[MemoryPackUnion(1, typeof(SumPointRecord))]
[MemoryPackUnion(2, typeof(HistogramPointRecord))]
[MemoryPackUnion(3, typeof(ExponentialHistogramPointRecord))]
public abstract partial record MetricPointRecord
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

    /// <summary>
    /// <c>Flare.Ingest</c>'s own wall-clock read, taken once per accepted OTLP export
    /// request and stamped on every data point it contains - not from the OTLP wire.
    /// Same field/rationale as <see cref="LogEvent.IngestedAt"/> - see that type's
    /// remarks and ADR-0014.
    /// </summary>
    public required DateTimeOffset IngestedAt { get; init; }
}

/// <summary>An OTLP Gauge data point - the "current value" at <see cref="MetricPointRecord.Time"/>, no aggregation temporality.</summary>
[MemoryPackable]
public sealed partial record GaugePointRecord : MetricPointRecord
{
    public required double Value { get; init; }
}

/// <summary>An OTLP Sum data point - a running total, either a monotonic counter or an up/down counter.</summary>
[MemoryPackable]
public sealed partial record SumPointRecord : MetricPointRecord
{
    public required double Value { get; init; }

    /// <summary>OTLP AggregationTemporality (0=unspecified, 1=delta, 2=cumulative).</summary>
    public required int AggregationTemporality { get; init; }

    public required bool IsMonotonic { get; init; }
}

/// <summary>An OTLP Histogram data point - bucketed distribution of a population of values.</summary>
[MemoryPackable]
public sealed partial record HistogramPointRecord : MetricPointRecord
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

/// <summary>
/// An OTLP ExponentialHistogram data point - a bucketed distribution whose bucket bounds are
/// implied by <see cref="Scale"/> rather than listed: bucket <c>i</c> covers
/// <c>(base^i, base^(i+1)]</c> with <c>base = 2^(2^-Scale)</c>. See ADR-0060.
/// </summary>
[MemoryPackable]
public sealed partial record ExponentialHistogramPointRecord : MetricPointRecord
{
    /// <summary>OTLP AggregationTemporality (0=unspecified, 1=delta, 2=cumulative).</summary>
    public required int AggregationTemporality { get; init; }

    public required ulong Count { get; init; }

    /// <summary>Optional per the OTLP spec - same null-maps-to-0 convention as <see cref="HistogramPointRecord.Sum"/>.</summary>
    public double? Sum { get; init; }

    /// <summary>Bucket resolution. The SDK lowers it on its own as the observed range widens, so it can differ between points of one series.</summary>
    public required int Scale { get; init; }

    /// <summary>Count of values whose absolute value is at most <see cref="ZeroThreshold"/>.</summary>
    public required ulong ZeroCount { get; init; }

    /// <summary>Width of the zero bucket; 0 (the wire default) means exact zeros only.</summary>
    public required double ZeroThreshold { get; init; }

    /// <summary>Bucket index of <see cref="PositiveBucketCounts"/>' first element.</summary>
    public required int PositiveOffset { get; init; }

    public required IReadOnlyList<ulong> PositiveBucketCounts { get; init; }

    /// <summary>Bucket index of <see cref="NegativeBucketCounts"/>' first element, indexed on the absolute value.</summary>
    public required int NegativeOffset { get; init; }

    public required IReadOnlyList<ulong> NegativeBucketCounts { get; init; }

    /// <summary>Optional per the OTLP spec; stored as NULL when absent.</summary>
    public double? Min { get; init; }

    /// <summary>Optional per the OTLP spec; stored as NULL when absent.</summary>
    public double? Max { get; init; }
}
