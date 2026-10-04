using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT</c> for the runtime-health series, ready to hand to <see cref="RuntimeHealthQueryService"/>.</summary>
public sealed record RuntimeHealthSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure SQL builder for the .NET runtime health detectors (<c>POST /api/services/runtime-health</c>).
/// Reads the <c>System.Runtime</c> meter's <c>dotnet.*</c> metrics already in <c>metrics_sum</c> -
/// no new table - and returns one <c>(Bucket, Metric, Instance, Value)</c> row per metric per
/// instance per time bucket. <see cref="RuntimeHealthDetector"/> turns those into findings.
/// </summary>
/// <remarks>
/// <para>
/// Counters (<see cref="CounterMetrics"/>) become the per-bucket increase, summed over each
/// instance's series, using the same reset-aware <c>lagInFrame</c> classification as
/// <see cref="HostInventoryQueryBuilder"/>'s CPU figure (ADR-0044): delta temporality taken
/// as-is, a series' first row in the window contributes 0, a negative delta is a restart and
/// counts the current value. The queue length is an UpDownCounter, so its row is the
/// per-bucket maximum, not an increase.
/// </para>
/// <para>
/// An instance is <c>service.instance.id</c>, else <c>k8s.pod.name</c>, else <c>host.name</c>:
/// detection is per instance so one starved replica isn't averaged away by healthy ones.
/// Buckets are about <see cref="TargetBuckets"/> across the window, never under 60s.
/// </para>
/// </remarks>
public static class RuntimeHealthQueryBuilder
{
    public const int DefaultWindowMinutes = 60;
    public const int MinWindowMinutes = 5;
    public const int MaxWindowMinutes = 1440;

    private const int TargetBuckets = 60;

    /// <summary>Hard cap on returned rows (instances x metrics x buckets).</summary>
    public const int MaxRows = 50_000;

    public const string QueueLengthMetric = "dotnet.thread_pool.queue.length";
    public const string WorkItemsMetric = "dotnet.thread_pool.work_item.count";
    public const string GcPauseMetric = "dotnet.gc.pause.time";
    public const string LockContentionsMetric = "dotnet.monitor.lock_contentions";
    public const string ExceptionsMetric = "dotnet.exceptions";

    /// <summary>Monotonic counters whose per-bucket increase the detectors read.</summary>
    public static readonly string[] CounterMetrics = [WorkItemsMetric, GcPauseMetric, LockContentionsMetric, ExceptionsMetric];

    private const string InstanceExpr =
        "multiIf(ResourceAttributes['service.instance.id'] != '', ResourceAttributes['service.instance.id'], " +
        "ResourceAttributes['k8s.pod.name'] != '', ResourceAttributes['k8s.pod.name'], ResourceAttributes['host.name'])";

    public static int ClampWindowMinutes(int? requested) =>
        Math.Clamp(requested is > 0 ? requested.Value : DefaultWindowMinutes, MinWindowMinutes, MaxWindowMinutes);

    /// <summary>About <see cref="TargetBuckets"/> buckets, rounded up to whole minutes, never under 60s.</summary>
    public static int BucketWidthSecondsFor(int windowMinutes) =>
        Math.Max(60, (int)Math.Ceiling(windowMinutes * 60.0 / TargetBuckets / 60.0) * 60);

    public static RuntimeHealthSql Build(string service, int windowMinutes, int bucketWidthSeconds, DateTimeOffset end)
    {
        var time = MetricFilterSqlBuilder.Build(
            new MetricFilter { From = end - TimeSpan.FromMinutes(windowMinutes), To = end, Services = [service] }, end);
        var parameters = time.Parameters;
        parameters.AddParameter("bucketWidth", (uint)bucketWidthSeconds);
        parameters.AddParameter("counterMetrics", CounterMetrics);
        parameters.AddParameter("queueMetric", QueueLengthMetric);
        parameters.AddParameter("limit", (uint)MaxRows);

        const string bucket = "toStartOfInterval(Time, INTERVAL {bucketWidth:UInt32} SECOND)";

        var sql =
            "SELECT Bucket, Metric, Instance, Value FROM (\n" +
            $"  SELECT {bucket} AS Bucket, Metric, Instance, sum(Delta) AS Value\n" +
            "  FROM (\n" +
            "    SELECT Metric, Instance, Time, multiIf(\n" +
            "      AggregationTemporality = 'AGGREGATION_TEMPORALITY_DELTA', Value,\n" +
            "      SeriesRowNum = 1, 0,\n" +
            "      RawDelta < 0, Value,\n" +
            "      RawDelta) AS Delta\n" +
            "    FROM (\n" +
            $"      SELECT MetricName AS Metric, {InstanceExpr} AS Instance, Time, Value, AggregationTemporality,\n" +
            "        row_number() OVER w AS SeriesRowNum,\n" +
            "        Value - lagInFrame(Value) OVER w AS RawDelta\n" +
            "      FROM metrics_sum\n" +
            $"      WHERE MetricName IN {{counterMetrics:Array(String)}} AND {time.WhereSql}\n" +
            $"      WINDOW w AS (PARTITION BY MetricName, {InstanceExpr}, toString(DataPointAttributes) ORDER BY Time)\n" +
            "    )\n" +
            "  )\n" +
            "  GROUP BY Bucket, Metric, Instance\n" +
            "  UNION ALL\n" +
            $"  SELECT {bucket} AS Bucket, MetricName AS Metric, {InstanceExpr} AS Instance, max(Value) AS Value\n" +
            "  FROM metrics_sum\n" +
            $"  WHERE MetricName = {{queueMetric:String}} AND {time.WhereSql}\n" +
            "  GROUP BY Bucket, Metric, Instance\n" +
            ")\n" +
            "ORDER BY Bucket, Metric, Instance\n" +
            "LIMIT {limit:UInt32}";

        return new RuntimeHealthSql(sql, parameters);
    }
}
