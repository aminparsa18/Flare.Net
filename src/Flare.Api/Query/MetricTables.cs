using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Shared <see cref="MetricPointType"/> → ClickHouse table name mapping (see
/// <c>db/clickhouse/0008_metrics.sql</c>'s "three tables, not one" design decision).
/// Used by <see cref="MetricSeriesQueryBuilder"/> and
/// <see cref="MetricAttributeKeysQueryBuilder"/>, both of which query one table per
/// request. <see cref="MetricNamesQueryBuilder"/> doesn't use this - it queries all three
/// tables at once via <c>UNION ALL</c>, one literal table name per branch, not a
/// request-driven single choice.
/// </summary>
internal static class MetricTables
{
    public static string For(MetricPointType type) => type switch
    {
        MetricPointType.Gauge => "metrics_gauge",
        MetricPointType.Sum => "metrics_sum",
        MetricPointType.Histogram => "metrics_histogram",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown metric point type."),
    };
}
