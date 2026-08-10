using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Pure <see cref="MetricPointRecord"/> → ClickHouse row mapping for the
/// <c>clickhousedb.metrics_gauge</c>/<c>metrics_sum</c>/<c>metrics_histogram</c> tables
/// (<c>db/clickhouse/0008_metrics.sql</c>). Deliberately has no ClickHouse connection
/// dependency, same style as <see cref="ClickHouseSpanRowMapper"/>.
/// </summary>
/// <remarks>
/// One mapper covering all three point types, not three separate classes: they share
/// every column up through <c>Time</c>, so <see cref="CommonValues"/> builds that shared
/// prefix once and each <c>To*Row</c> method appends its own type-specific tail - avoids
/// tripling the shared-column boilerplate for what's still one signal (see
/// <see cref="MetricPointRecord"/>'s own remarks for the same reasoning applied to the
/// C# model).
///
/// <c>AggregationTemporality</c> is inserted as the enum's string label (e.g.
/// <c>"AGGREGATION_TEMPORALITY_CUMULATIVE"</c>), not its ordinal byte - same
/// insert/read-symmetry reasoning as <see cref="ClickHouseSpanRowMapper"/>'s
/// <c>StatusCode</c> handling. <c>BucketCounts</c>/<c>ExplicitBounds</c> round-trip as
/// plain <c>ulong[]</c>/<c>double[]</c> - confirmed via the same live spike against
/// <c>Aspire.ClickHouse.Driver</c> documented in <c>0008_metrics.sql</c>.
/// </remarks>
public static class ClickHouseMetricRowMapper
{
    private static readonly string[] AggregationTemporalityLabels =
    [
        "AGGREGATION_TEMPORALITY_UNSPECIFIED",
        "AGGREGATION_TEMPORALITY_DELTA",
        "AGGREGATION_TEMPORALITY_CUMULATIVE",
    ];

    /// <summary>Columns shared by all three tables, in DDL declaration order - the prefix every row starts with.</summary>
    private static readonly string[] CommonColumns =
    [
        "MetricName",
        "Description",
        "Unit",
        "ServiceName",
        "ResourceSchemaUrl",
        "ResourceAttributes",
        "ScopeSchemaUrl",
        "ScopeName",
        "ScopeVersion",
        "ScopeAttributes",
        "DataPointAttributes",
        "StartTime",
        "Time",
    ];

    public static readonly IReadOnlyList<string> GaugeColumns = [.. CommonColumns, "Value"];

    public static readonly IReadOnlyList<string> SumColumns = [.. CommonColumns, "Value", "AggregationTemporality", "IsMonotonic"];

    public static readonly IReadOnlyList<string> HistogramColumns =
        [.. CommonColumns, "AggregationTemporality", "Count", "Sum", "BucketCounts", "ExplicitBounds"];

    public static object[] ToRow(GaugePointRecord point) => [.. CommonValues(point), point.Value];

    public static object[] ToRow(SumPointRecord point) =>
        [.. CommonValues(point), point.Value, AggregationTemporalityLabel(point.AggregationTemporality), (byte)(point.IsMonotonic ? 1 : 0)];

    public static object[] ToRow(HistogramPointRecord point) =>
    [
        .. CommonValues(point),
        AggregationTemporalityLabel(point.AggregationTemporality),
        point.Count,
        point.Sum ?? 0d,
        point.BucketCounts.ToArray(),
        point.ExplicitBounds.ToArray(),
    ];

    public static IReadOnlyList<object[]> ToRows(IReadOnlyList<GaugePointRecord> points) => [.. points.Select(ToRow)];

    public static IReadOnlyList<object[]> ToRows(IReadOnlyList<SumPointRecord> points) => [.. points.Select(ToRow)];

    public static IReadOnlyList<object[]> ToRows(IReadOnlyList<HistogramPointRecord> points) => [.. points.Select(ToRow)];

    /// <summary>Builds the shared column-prefix values, positionally matching <see cref="CommonColumns"/>.</summary>
    private static object[] CommonValues(MetricPointRecord point) =>
    [
        point.MetricName,
        point.Description ?? string.Empty,
        point.Unit ?? string.Empty,
        point.ServiceName ?? string.Empty,
        point.ResourceSchemaUrl ?? string.Empty,
        new Dictionary<string, string>(point.ResourceAttributes),
        point.ScopeSchemaUrl ?? string.Empty,
        point.ScopeName ?? string.Empty,
        point.ScopeVersion ?? string.Empty,
        new Dictionary<string, string>(point.ScopeAttributes),
        new Dictionary<string, string>(point.DataPointAttributes),
        // StartTime coalesces to Time when the wire didn't set it - see
        // MetricPointRecord.StartTime's remarks.
        (point.StartTime ?? point.Time).UtcDateTime,
        point.Time.UtcDateTime,
    ];

    /// <summary>
    /// Maps OTLP's AggregationTemporality int (0=unspecified, 1=delta, 2=cumulative) to
    /// the DDL's Enum8 label. Falls back to unspecified for any out-of-range value rather
    /// than throwing - same forward-compat guard as
    /// <see cref="ClickHouseSpanRowMapper"/>'s StatusCode mapping.
    /// </summary>
    private static string AggregationTemporalityLabel(int aggregationTemporality) =>
        aggregationTemporality >= 0 && aggregationTemporality < AggregationTemporalityLabels.Length
            ? AggregationTemporalityLabels[aggregationTemporality]
            : AggregationTemporalityLabels[0];
}
