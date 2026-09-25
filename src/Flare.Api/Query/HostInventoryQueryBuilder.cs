using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT</c> for one of the Hosts page's queries, ready to hand to <see cref="HostInventoryQueryService"/>.</summary>
public sealed record HostInventorySql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure SQL builder for the Hosts page (<c>POST /api/hosts</c> and
/// <c>POST /api/hosts/metrics</c>) - a host inventory derived entirely from OTel
/// <c>hostmetrics</c>-receiver metrics already sitting in <c>metrics_gauge</c>/
/// <c>metrics_sum</c>, no new table or ingest-side change. Same "pure function, no
/// ClickHouse dependency" style as the other builders.
/// </summary>
/// <remarks>
/// <para>
/// <b>Host identity</b> is the <c>host.name</c> resource attribute; a data point without
/// one is ignored. A "host" is anything that sent at least one <c>system.*</c> metric in
/// the window - <c>MetricName LIKE 'system.%'</c> is a prefix match on the tables' leading
/// <c>ORDER BY</c> column, so it prunes by primary key rather than scanning every metric.
/// </para>
/// <para>
/// <b>Which metric each column comes from</b> - only the <c>hostmetrics</c> receiver's
/// <em>default-enabled</em> metrics, so a stock collector config fills every column. The
/// opt-in <c>*.utilization</c> gauges are not read.
/// <list type="bullet">
/// <item><b>CPU %</b>: <c>system.cpu.time</c> (a cumulative per-<c>cpu</c>/<c>state</c>
/// seconds counter) - non-idle increase over total increase. Each counter's per-row
/// increase uses the same reset-aware <c>lagInFrame</c> classification as
/// <see cref="MetricSeriesQueryBuilder"/>'s Sum shape (ADR-0044): delta temporality taken
/// as-is, a counter's first row in the window contributes 0, a negative delta is a reset
/// and counts the current value.</item>
/// <item><b>Memory %</b>: <c>system.memory.usage</c> - <c>used</c> over the sum of every
/// state except <c>slab_reclaimable</c>/<c>slab_unreclaimable</c>, which on Linux are
/// subsets of <c>cached</c>/<c>used</c> and would double-count toward the total.</item>
/// <item><b>Disk %</b>: <c>system.filesystem.usage</c> - <c>used</c> over the sum of every
/// state (<c>used</c>/<c>free</c>/<c>reserved</c>), summed across every reported
/// filesystem.</item>
/// <item><b>Load</b>: <c>system.cpu.load_average.15m</c>, averaged.</item>
/// </list>
/// Memory/disk compute a ratio per scrape (<c>GROUP BY Host, Time</c> - every state of one
/// scrape shares its timestamp) and then average those ratios, so a single scrape that
/// reported only some states can't skew the result the way summing numerator and
/// denominator separately over the window would.
/// </para>
/// <para>
/// <b>Shape</b>: the list endpoint runs two statements - <see cref="BuildHostList"/>
/// (which hosts exist, capped at <see cref="MaxHosts"/>) then <see cref="BuildListValues"/>
/// (the four figures for exactly those hosts, as long-format <c>(Key, Kind, Value)</c>
/// rows). The drill-down (<see cref="BuildHostMetrics"/>) reuses the same four per-kind
/// subqueries, keyed by time bucket instead of by host.
/// </para>
/// </remarks>
public static class HostInventoryQueryBuilder
{
    public const int DefaultWindowMinutes = 60;
    public const int MinWindowMinutes = 5;
    public const int MaxWindowMinutes = 1440;

    /// <summary>Row cap on the host list - a table, not a chart, but still bounded like every other query here.</summary>
    public const int MaxHosts = 500;

    /// <summary>Drill-down charts target roughly this many buckets.</summary>
    private const int TargetBuckets = 60;

    public const string CpuKind = "cpu";
    public const string MemoryKind = "memory";
    public const string DiskKind = "disk";
    public const string LoadKind = "load15";

    private const string HostExpr = "ResourceAttributes['host.name']";

    /// <summary>Clamps a caller-supplied window, defaulting a missing/non-positive one - same shape as <see cref="ServiceOverviewQueryBuilder.ClampWindowMinutes"/>.</summary>
    public static int ClampWindowMinutes(int? requested) =>
        Math.Clamp(requested is > 0 ? requested.Value : DefaultWindowMinutes, MinWindowMinutes, MaxWindowMinutes);

    /// <summary>
    /// The drill-down window's end: <see cref="HostMetricsRequest.EndUnixMs"/> when it's a
    /// representable instant, otherwise <paramref name="now"/> - lenient like
    /// <see cref="ClampWindowMinutes"/>, rather than failing the request. A future end is
    /// allowed (a window centred on a log from a minute ago extends past now); the empty
    /// tail just has no buckets.
    /// </summary>
    public static DateTimeOffset ResolveWindowEnd(long? endUnixMs, DateTimeOffset now) =>
        endUnixMs is { } ms && ms > 0 && ms <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : now;

    /// <summary>
    /// Bucket width for the drill-down: about <see cref="TargetBuckets"/> buckets, rounded up
    /// to a whole minute and never under 60s - the <c>hostmetrics</c> receiver's default
    /// collection interval, below which buckets would just alternate between one scrape and
    /// none.
    /// </summary>
    public static int BucketWidthSecondsFor(int windowMinutes) =>
        Math.Max(60, (int)Math.Ceiling(windowMinutes * 60.0 / TargetBuckets / 60.0) * 60);

    /// <summary>Distinct hosts in the window with their <c>os.type</c> and last-seen time, filtered and capped (fetches <see cref="MaxHosts"/> + 1 rows so the caller can tell it was cut).</summary>
    public static HostInventorySql BuildHostList(HostListRequest request, int windowMinutes, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        var parameters = time.Parameters;
        parameters.AddParameter("hostLimit", (uint)(MaxHosts + 1));

        var extra = "";
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            parameters.AddParameter("search", request.Search.Trim());
            extra += " AND positionCaseInsensitiveUTF8(Host, {search:String}) > 0";
        }

        if (!string.IsNullOrWhiteSpace(request.OsType))
        {
            parameters.AddParameter("osType", request.OsType.Trim());
            extra += " AND OsType = {osType:String}";
        }

        string Branch(string table) =>
            $"  SELECT {HostExpr} AS Host, ResourceAttributes['os.type'] AS OsType, max(Time) AS LastSeen\n" +
            $"  FROM {table}\n" +
            $"  WHERE MetricName LIKE 'system.%' AND {time.WhereSql} AND Host != ''{extra}\n" +
            "  GROUP BY Host, OsType\n";

        // Outer aliases deliberately differ from the inner column names: ClickHouse
        // resolves an identifier to a same-named SELECT alias first, so
        // `argMax(OsType, LastSeen) ... max(LastSeen) AS LastSeen` reads the argMax's
        // LastSeen as the outer max() - an aggregate inside an aggregate, rejected with
        // ILLEGAL_AGGREGATION (caught live, not by the SQL-substring unit tests).
        var sql = "SELECT Host, argMax(OsType, LastSeen) AS LatestOsType, max(LastSeen) AS LatestSeen\n" +
            "FROM (\n" +
            Branch("metrics_gauge") +
            "  UNION ALL\n" +
            Branch("metrics_sum") +
            ")\n" +
            "GROUP BY Host\n" +
            "ORDER BY Host\n" +
            "LIMIT {hostLimit:UInt32}";

        return new HostInventorySql(sql, parameters);
    }

    /// <summary>Whole-window CPU/memory/disk/load figures for <paramref name="hosts"/>, one <c>(Key = host, Kind, Value)</c> row per host per kind that had data.</summary>
    public static HostInventorySql BuildListValues(IReadOnlyList<string> hosts, int windowMinutes, DateTimeOffset now)
    {
        var time = TimeFilter(windowMinutes, now);
        time.Parameters.AddParameter("hosts", hosts.ToArray());

        return new HostInventorySql(
            BuildValuesUnion("Host", $"{HostExpr} IN {{hosts:Array(String)}}", time.WhereSql),
            time.Parameters);
    }

    /// <summary>One host's four figures per time bucket over the <paramref name="windowMinutes"/> ending at <paramref name="end"/>, as <c>(Key = BucketStart, Kind, Value)</c> rows.</summary>
    public static HostInventorySql BuildHostMetrics(string hostName, int windowMinutes, int bucketWidthSeconds, DateTimeOffset end)
    {
        var time = TimeFilter(windowMinutes, end);
        time.Parameters.AddParameter("hostName", hostName);
        time.Parameters.AddParameter("bucketWidth", (uint)bucketWidthSeconds);

        return new HostInventorySql(
            BuildValuesUnion("toStartOfInterval(Time, INTERVAL {bucketWidth:UInt32} SECOND)", $"{HostExpr} = {{hostName:String}}", time.WhereSql),
            time.Parameters);
    }

    private static MetricFilterSql TimeFilter(int windowMinutes, DateTimeOffset now) =>
        MetricFilterSqlBuilder.Build(new MetricFilter { From = now - TimeSpan.FromMinutes(windowMinutes), To = now }, now);

    /// <summary>
    /// The four per-kind subqueries, each exposing <c>Host</c>/<c>Time</c> so
    /// <paramref name="keyExpr"/> can group by either, <c>UNION ALL</c>'d into long format.
    /// Every <c>Value</c> is cast to <c>Nullable(Float64)</c> so the branches share one type
    /// (CPU/memory/disk divide by <c>nullIf(..., 0)</c>; load is a plain average).
    /// </summary>
    private static string BuildValuesUnion(string keyExpr, string hostWhere, string timeWhere)
    {
        var where = $"{timeWhere} AND {hostWhere}";

        var cpuInner =
            "SELECT Host, Time, State, multiIf(\n" +
            "      AggregationTemporality = 'AGGREGATION_TEMPORALITY_DELTA', Value,\n" +
            "      SeriesRowNum = 1, 0,\n" +
            "      RawDelta < 0, Value,\n" +
            "      RawDelta) AS Delta\n" +
            "    FROM (\n" +
            $"      SELECT {HostExpr} AS Host, Time, DataPointAttributes['state'] AS State, Value, AggregationTemporality,\n" +
            "        row_number() OVER w AS SeriesRowNum,\n" +
            "        Value - lagInFrame(Value) OVER w AS RawDelta\n" +
            "      FROM metrics_sum\n" +
            $"      WHERE MetricName = 'system.cpu.time' AND {where}\n" +
            $"      WINDOW w AS (PARTITION BY {HostExpr}, ServiceName, toString(DataPointAttributes) ORDER BY Time)\n" +
            "    )";

        var memoryInner =
            $"SELECT {HostExpr} AS Host, Time,\n" +
            "      sumIf(Value, DataPointAttributes['state'] = 'used')\n" +
            "        / nullIf(sumIf(Value, DataPointAttributes['state'] NOT IN ('slab_reclaimable', 'slab_unreclaimable')), 0) AS Ratio\n" +
            "    FROM metrics_sum\n" +
            $"    WHERE MetricName = 'system.memory.usage' AND {where}\n" +
            "    GROUP BY Host, Time";

        var diskInner =
            $"SELECT {HostExpr} AS Host, Time,\n" +
            "      sumIf(Value, DataPointAttributes['state'] = 'used') / nullIf(sum(Value), 0) AS Ratio\n" +
            "    FROM metrics_sum\n" +
            $"    WHERE MetricName = 'system.filesystem.usage' AND {where}\n" +
            "    GROUP BY Host, Time";

        var loadInner =
            $"SELECT {HostExpr} AS Host, Time, Value\n" +
            "    FROM metrics_gauge\n" +
            $"    WHERE MetricName = 'system.cpu.load_average.15m' AND {where}";

        string Branch(string kind, string aggregate, string inner) =>
            $"SELECT {keyExpr} AS Key, '{kind}' AS Kind, CAST({aggregate} AS Nullable(Float64)) AS Value\n" +
            $"  FROM (\n    {inner}\n  )\n" +
            "  GROUP BY Key\n";

        return Branch(CpuKind, "100 * sumIf(Delta, State != 'idle') / nullIf(sum(Delta), 0)", cpuInner) +
            "UNION ALL\n" +
            Branch(MemoryKind, "100 * avg(Ratio)", memoryInner) +
            "UNION ALL\n" +
            Branch(DiskKind, "100 * avg(Ratio)", diskInner) +
            "UNION ALL\n" +
            Branch(LoadKind, "avg(Value)", loadInner);
    }
}
