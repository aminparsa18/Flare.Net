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
/// </remarks>
public sealed class IndexingQueryService(IClickHouseClient client, ILogger<IndexingQueryService> logger) : IIndexingQueryService
{
    private const string TablesSql = """
        SELECT name, engine, sorting_key, total_rows, total_bytes, total_bytes_uncompressed, active_parts
        FROM system.tables
        WHERE database = currentDatabase()
        ORDER BY total_bytes DESC
        """;

    private const string SkipIndexesSql = """
        SELECT table, name, type, expr, granularity, data_compressed_bytes, data_uncompressed_bytes
        FROM system.data_skipping_indices
        WHERE database = currentDatabase()
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

    public async Task<IndexingStatsResponse> GetStatsAsync(CancellationToken cancellationToken)
    {
        var tablesTask = ReadTablesAsync(cancellationToken);
        var skipIndexesTask = ReadSkipIndexesAsync(cancellationToken);
        var growthTask = ReadGrowthAsync(cancellationToken);

        await Task.WhenAll(tablesTask, skipIndexesTask, growthTask);

        var growth = await growthTask;
        return new IndexingStatsResponse(
            DateTimeOffset.UtcNow,
            await tablesTask,
            await skipIndexesTask,
            growth.Points,
            growth.Available);
    }

    private async Task<IReadOnlyList<TableStorageInfo>> ReadTablesAsync(CancellationToken cancellationToken)
    {
        await using var reader = await client.ExecuteReaderAsync(TablesSql, null, SafetyOptions(), cancellationToken);
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
        await using var reader = await client.ExecuteReaderAsync(SkipIndexesSql, null, SafetyOptions(), cancellationToken);
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
            await using var reader = await client.ExecuteReaderAsync(GrowthSql, null, SafetyOptions(), cancellationToken);
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
