using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using Flare.Api.Model;

namespace Flare.Api.Query;

public interface IIndexingQueryService
{
    Task<IndexingStatsResponse> GetStatsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Backs the dashboard's Indexing page - Flare's answer to Seq's own "Indexing" screen,
/// reworked for what ClickHouse actually is: unlike Seq's embedded store (where a user
/// explicitly creates computed/signal indexes and this page shows what that costs), every
/// index here is schema-defined at migration time (see db/clickhouse/*.sql) - there's
/// nothing for a self-hosted user to create or tune. What's genuinely useful instead is
/// visibility ClickHouse operators normally only get via <c>clickhouse-client</c>: per-table
/// storage/compression, the skip-index inventory behind fast trace/attribute lookups, and a
/// growth trend - all three come straight from ClickHouse's own <c>system.*</c> tables, no
/// new storage of our own.
/// </summary>
/// <remarks>
/// No filter/request DTO and so no accompanying SQL-builder class the way
/// <see cref="LogQueryService"/>/<see cref="SpanQueryService"/> have one - this endpoint
/// takes no input, three fixed queries scoped to <c>currentDatabase()</c> (Flare owns its
/// whole ClickHouse database, so no table allowlist is needed - new migrations show up
/// automatically). Queried concurrently since they're independent reads.
///
/// <para>
/// <paramref name="clusterMode"/> (same <c>ClickHouse:ClusterMode</c> flag
/// <c>ClickHouseMigrationRunner</c> reads - see docs/clustering.md) switches every query
/// below from plain <c>system.*</c> (this one connected node's local state) to the
/// <c>'flare_cluster'</c>-scoped variant, so the Indexing page reflects the whole
/// cluster rather than whichever of the 4 nodes the connection happened to land on:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>system.tables</c>/<c>system.data_skipping_indices</c> back <c>ReplicatedMergeTree</c>
/// storage that's identical across a shard's replicas - <c>cluster()</c> (one replica per
/// shard, not <c>clusterAllReplicas()</c>) avoids double-counting a shard's own data twice,
/// then the two shards' worth get summed via <c>GROUP BY</c>.
/// </description></item>
/// <item><description>
/// <c>system.part_log</c> similarly uses <c>cluster()</c> - both replicas of a shard log
/// their own <c>NewPart</c> event for what's ultimately the same replicated part (one from
/// the original insert/merge, the other from the replication fetch), so querying every
/// replica here would double the growth trend the same way.
/// </description></item>
/// <item><description>
/// <c>system.disks</c>/<c>system.query_log</c> aren't replicated - each of the 4 nodes has
/// its own physical disk and its own independent query traffic, so these use
/// <c>clusterAllReplicas()</c> to cover every node rather than every shard.
/// </description></item>
/// </list>
/// </remarks>
public sealed class IndexingQueryService(IClickHouseClient client, ILogger<IndexingQueryService> logger, bool clusterMode) : IIndexingQueryService
{
    // Matches the literal 'flare_cluster' name defined in db/clickhouse-cluster's
    // remote-servers.xml and used by every ON CLUSTER statement in db/clickhouse-cluster/
    // *.sql / ClickHouseMigrationRunner - no shared constant between them (same
    // hand-synced-literal spirit as SlowQueryThresholdMs below), just kept consistent.
    private const string TablesSql = """
        SELECT name, engine, sorting_key, total_rows, total_bytes, total_bytes_uncompressed, active_parts
        FROM system.tables
        WHERE database = currentDatabase()
        ORDER BY total_bytes DESC
        """;

    private const string TablesClusterSql = """
        SELECT name, any(engine) AS engine, any(sorting_key) AS sorting_key,
               sum(total_rows) AS total_rows, sum(total_bytes) AS total_bytes,
               sum(total_bytes_uncompressed) AS total_bytes_uncompressed, sum(active_parts) AS active_parts
        FROM cluster('flare_cluster', system.tables)
        WHERE database = currentDatabase()
        GROUP BY name
        ORDER BY total_bytes DESC
        """;

    private const string SkipIndexesSql = """
        SELECT table, name, type, expr, granularity, data_compressed_bytes, data_uncompressed_bytes
        FROM system.data_skipping_indices
        WHERE database = currentDatabase()
        ORDER BY table, name
        """;

    private const string SkipIndexesClusterSql = """
        SELECT table, name, any(type) AS type, any(expr) AS expr, any(granularity) AS granularity,
               sum(data_compressed_bytes) AS data_compressed_bytes, sum(data_uncompressed_bytes) AS data_uncompressed_bytes
        FROM cluster('flare_cluster', system.data_skipping_indices)
        WHERE database = currentDatabase()
        GROUP BY table, name
        ORDER BY table, name
        """;

    // Bounded to 30 days regardless of system.part_log's own retention/TTL config - a
    // growth trend has no use for more than that, and this keeps the query cheap on a
    // long-lived deployment where part_log itself might hold much more.
    private const string GrowthSql = """
        SELECT toDate(event_time) AS day, table, sum(size_in_bytes) AS bytes, sum(rows) AS rows
        FROM system.part_log
        WHERE database = currentDatabase() AND event_type = 'NewPart' AND event_time >= now() - INTERVAL 30 DAY
        GROUP BY day, table
        ORDER BY day, table
        """;

    private const string GrowthClusterSql = """
        SELECT toDate(event_time) AS day, table, sum(size_in_bytes) AS bytes, sum(rows) AS rows
        FROM cluster('flare_cluster', system.part_log)
        WHERE database = currentDatabase() AND event_type = 'NewPart' AND event_time >= now() - INTERVAL 30 DAY
        GROUP BY day, table
        ORDER BY day, table
        """;

    // Largest disk wins when more than one is mounted - see DiskUsageInfo's doc comment.
    private const string DiskUsageSql = """
        SELECT total_space, free_space
        FROM system.disks
        ORDER BY total_space DESC
        LIMIT 1
        """;

    // Same "largest disk wins" per node, but summed across all 4 nodes instead of just
    // the one connected to - `LIMIT 1 BY host` picks each node's own largest disk before
    // the outer sum() combines them, so a node with two mounted disks doesn't get
    // double-counted against a node with one.
    private const string DiskUsageClusterSql = """
        SELECT sum(total_space) AS total_space, sum(free_space) AS free_space
        FROM
        (
            SELECT hostName() AS host, total_space, free_space
            FROM clusterAllReplicas('flare_cluster', system.disks)
            ORDER BY host, total_space DESC
            LIMIT 1 BY host
        )
        """;

    private const int QueryPerformanceWindowMinutes = 60;

    // Threaded through to QueryPerformanceInfo so the dashboard's "> 500 ms" slow-query
    // label reads this constant instead of hardcoding its own copy - but note the `500`
    // literal inside QueryPerformanceSql/QueryPerformanceClusterSql below is a separate
    // copy (ClickHouse SQL text can't reference a C# const), so all three have to be kept
    // in sync by hand if this ever changes.
    private const int SlowQueryThresholdMs = 500;

    // `NOT has(databases, 'system')` excludes this very service's own introspection
    // queries (and any other system-table tooling) - what's left is queries real dashboard
    // usage generated (log search, trace lookups, the Query API generally).
    private const string QueryPerformanceSql = """
        SELECT
            quantile(0.5)(query_duration_ms) AS p50,
            quantile(0.95)(query_duration_ms) AS p95,
            quantile(0.99)(query_duration_ms) AS p99,
            countIf(query_duration_ms > 500) AS slow,
            count() AS samples
        FROM system.query_log
        WHERE type = 'QueryFinish'
          AND event_time >= now() - INTERVAL 60 MINUTE
          AND has(databases, currentDatabase())
          AND NOT has(databases, 'system')
        """;

    // quantile()/count() merge correctly over a distributed table function's per-node
    // partial states the same way they would over a Distributed table - ClickHouse
    // computes the real cluster-wide percentiles here, not an average-of-per-node-p50s.
    private const string QueryPerformanceClusterSql = """
        SELECT
            quantile(0.5)(query_duration_ms) AS p50,
            quantile(0.95)(query_duration_ms) AS p95,
            quantile(0.99)(query_duration_ms) AS p99,
            countIf(query_duration_ms > 500) AS slow,
            count() AS samples
        FROM clusterAllReplicas('flare_cluster', system.query_log)
        WHERE type = 'QueryFinish'
          AND event_time >= now() - INTERVAL 60 MINUTE
          AND has(databases, currentDatabase())
          AND NOT has(databases, 'system')
        """;

    public async Task<IndexingStatsResponse> GetStatsAsync(CancellationToken cancellationToken)
    {
        var tablesTask = ReadTablesAsync(cancellationToken);
        var skipIndexesTask = ReadSkipIndexesAsync(cancellationToken);
        var growthTask = ReadGrowthAsync(cancellationToken);
        var diskUsageTask = ReadDiskUsageAsync(cancellationToken);
        var queryPerformanceTask = ReadQueryPerformanceAsync(cancellationToken);

        await Task.WhenAll(tablesTask, skipIndexesTask, growthTask, diskUsageTask, queryPerformanceTask);

        var growth = await growthTask;
        return new IndexingStatsResponse(
            DateTimeOffset.UtcNow,
            await tablesTask,
            await skipIndexesTask,
            growth.Points,
            growth.Available,
            await diskUsageTask,
            await queryPerformanceTask);
    }

    private async Task<IReadOnlyList<TableStorageInfo>> ReadTablesAsync(CancellationToken cancellationToken)
    {
        await using var reader = await client.ExecuteReaderAsync(clusterMode ? TablesClusterSql : TablesSql, null, SafetyOptions(), cancellationToken);
        var tables = new List<TableStorageInfo>();
        while (reader.Read())
        {
            tables.Add(new TableStorageInfo(
                TableName: reader.GetString(0),
                Engine: reader.GetString(1),
                SortingKey: reader.GetString(2),
                Rows: ReadNullableUInt64(reader, 3),
                CompressedBytes: ReadNullableUInt64(reader, 4),
                UncompressedBytes: ReadNullableUInt64(reader, 5),
                ActiveParts: ReadNullableUInt64(reader, 6)));
        }

        return tables;
    }

    private async Task<IReadOnlyList<SkipIndexInfo>> ReadSkipIndexesAsync(CancellationToken cancellationToken)
    {
        await using var reader = await client.ExecuteReaderAsync(clusterMode ? SkipIndexesClusterSql : SkipIndexesSql, null, SafetyOptions(), cancellationToken);
        var indexes = new List<SkipIndexInfo>();
        while (reader.Read())
        {
            indexes.Add(new SkipIndexInfo(
                TableName: reader.GetString(0),
                IndexName: reader.GetString(1),
                Type: reader.GetString(2),
                Expression: reader.GetString(3),
                Granularity: (long)reader.GetFieldValue<ulong>(4),
                CompressedBytes: (long)reader.GetFieldValue<ulong>(5),
                UncompressedBytes: (long)reader.GetFieldValue<ulong>(6)));
        }

        return indexes;
    }

    /// <summary>
    /// <c>system.part_log</c> is config-gated in ClickHouse, not guaranteed present on
    /// every self-hosted deployment - any failure here degrades to an empty, marked-
    /// unavailable growth series rather than failing the whole stats response.
    /// </summary>
    private async Task<(IReadOnlyList<StorageGrowthPoint> Points, bool Available)> ReadGrowthAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var reader = await client.ExecuteReaderAsync(clusterMode ? GrowthClusterSql : GrowthSql, null, SafetyOptions(), cancellationToken);
            var points = new List<StorageGrowthPoint>();
            while (reader.Read())
            {
                points.Add(new StorageGrowthPoint(
                    Day: ReadUtcDate(reader, 0),
                    TableName: reader.GetString(1),
                    Bytes: (long)reader.GetFieldValue<ulong>(2),
                    Rows: (long)reader.GetFieldValue<ulong>(3)));
            }

            return (points, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Indexing page's growth trend unavailable - system.part_log isn't queryable on this ClickHouse deployment");
            return ([], false);
        }
    }

    /// <summary>See <see cref="DiskUsageInfo"/> for why this degrades rather than fails the response.</summary>
    private async Task<DiskUsageInfo> ReadDiskUsageAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var reader = await client.ExecuteReaderAsync(clusterMode ? DiskUsageClusterSql : DiskUsageSql, null, SafetyOptions(), cancellationToken);

            // DiskUsageClusterSql is a bare sum() with no GROUP BY, so it always returns
            // exactly one row even when clusterAllReplicas() found zero disks across every
            // node - that row's sum columns are NULL rather than the row being absent, unlike
            // DiskUsageSql's plain SELECT ... LIMIT 1 (absent row = !reader.Read()). Cover
            // both shapes: no row, or a row of NULLs, both mean "unavailable".
            if (!reader.Read() || reader.IsDBNull(0))
            {
                return new DiskUsageInfo(Available: false, TotalBytes: 0, FreeBytes: 0);
            }

            return new DiskUsageInfo(
                Available: true,
                TotalBytes: (long)reader.GetFieldValue<ulong>(0),
                FreeBytes: (long)reader.GetFieldValue<ulong>(1));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Indexing page's disk usage unavailable - system.disks isn't queryable on this ClickHouse deployment");
            return new DiskUsageInfo(Available: false, TotalBytes: 0, FreeBytes: 0);
        }
    }

    /// <summary>
    /// <c>system.query_log</c> is config-gated in ClickHouse just like <c>system.part_log</c>
    /// (see <see cref="ReadGrowthAsync"/>) - degrades the same way on failure.
    /// </summary>
    private async Task<QueryPerformanceInfo> ReadQueryPerformanceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var reader = await client.ExecuteReaderAsync(clusterMode ? QueryPerformanceClusterSql : QueryPerformanceSql, null, SafetyOptions(), cancellationToken);
            if (!reader.Read())
            {
                return EmptyQueryPerformance(available: true);
            }

            // quantile() over zero rows returns NaN, not null/0 - gate every percentile on
            // sampleCount rather than trusting IsDBNull alone.
            var sampleCount = (long)reader.GetFieldValue<ulong>(4);
            double? Percentile(int ordinal) => sampleCount > 0 && !reader.IsDBNull(ordinal) ? reader.GetDouble(ordinal) : null;

            return new QueryPerformanceInfo(
                Available: true,
                P50Ms: Percentile(0),
                P95Ms: Percentile(1),
                P99Ms: Percentile(2),
                SlowQueryCount: (long)reader.GetFieldValue<ulong>(3),
                SampleCount: sampleCount,
                QueryPerformanceWindowMinutes,
                SlowQueryThresholdMs);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Indexing page's query performance unavailable - system.query_log isn't queryable on this ClickHouse deployment");
            return EmptyQueryPerformance(available: false);
        }
    }

    private static QueryPerformanceInfo EmptyQueryPerformance(bool available) => new(
        available,
        P50Ms: null,
        P95Ms: null,
        P99Ms: null,
        SlowQueryCount: 0,
        SampleCount: 0,
        QueryPerformanceWindowMinutes,
        SlowQueryThresholdMs);

    private static long ReadNullableUInt64(ClickHouseDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0L : (long)reader.GetFieldValue<ulong>(ordinal);

    private static DateTimeOffset ReadUtcDate(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    /// <summary>Same query-safety rationale as <see cref="LogQueryService.SafetyOptions"/> - these are system-table reads, not user-filtered data, but the same execution-time/row caps are cheap insurance.</summary>
    private static QueryOptions SafetyOptions() => new()
    {
        CustomSettings = new Dictionary<string, object>
        {
            ["max_execution_time"] = 30,
            ["timeout_before_checking_execution_speed"] = 0,
        },
    };
}
