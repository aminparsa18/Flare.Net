using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using Flare.Api.Model;
using Flare.Identity.IngestKeys;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IUsageQueryService
{
    Task<UsageResponse> GetAsync(int days, CancellationToken cancellationToken);
}

/// <summary>
/// Backs the Usage page: per-service and per-ingest-key volume plus the largest attribute
/// keys, so users can see what is driving storage before choosing sampling or retention.
/// Nothing new is stored - events/day and per-service counts are <c>count()</c> over the
/// signal tables, table sizes come from <see cref="IIndexingQueryService"/> (which already
/// owns the single-node vs cluster <c>system.tables</c> switch), and per-key volume is the
/// Redis day counter ADR-0051 already keeps.
/// </summary>
/// <remarks>
/// Counts are stored rows, so trace-sampled spans (ADR-0122) count once. Attribute sizes come
/// from a bounded sample of the latest day's rows (<see cref="AttributeSampleRows"/> per
/// source) and are uncompressed key+value bytes - a ranking, not an exact disk figure.
/// Any attribute-sample failure degrades to an empty list rather than failing the page.
/// </remarks>
public sealed class UsageQueryService(
    IClickHouseClient client,
    IIndexingQueryService indexing,
    IIngestApiKeyStore ingestKeys,
    IIngestApiKeyUsageQueryService ingestKeyUsage,
    IOptions<QueryLimitsOptions> queryLimits,
    ILogger<UsageQueryService> logger,
    TimeProvider timeProvider) : IUsageQueryService
{
    internal const int AttributeSampleRows = 100_000;

    // (signal label, table, timestamp column, extra tables UNION-ed for the same signal)
    private static readonly (string Signal, string[] Tables, string TimeColumn)[] Sources =
    [
        ("Logs", ["logs"], "Timestamp"),
        ("Traces", ["spans"], "StartTime"),
        ("Metrics", ["metrics_gauge", "metrics_sum", "metrics_histogram", "metrics_exponential_histogram"], "Time"),
    ];

    // (signal, scope, table, map column, timestamp column)
    private static readonly (string Signal, string Scope, string Table, string Column, string TimeColumn)[] AttributeSources =
    [
        ("Logs", "Log", "logs", "LogAttributes", "Timestamp"),
        ("Logs", "Resource", "logs", "ResourceAttributes", "Timestamp"),
        ("Traces", "Span", "spans", "SpanAttributes", "StartTime"),
        ("Metrics", "DataPoint", "metrics_sum", "DataPointAttributes", "Time"),
    ];

    public async Task<UsageResponse> GetAsync(int days, CancellationToken cancellationToken)
    {
        days = UsageShaper.ClampDays(days);

        var volumeTask = ReadVolumeAsync(days, cancellationToken);
        var storageTask = indexing.GetStatsAsync(cancellationToken);
        var attributesTask = ReadAttributesAsync(cancellationToken);
        var keysTask = ReadIngestKeysAsync(cancellationToken);
        await Task.WhenAll(volumeTask, storageTask, attributesTask, keysTask);

        var (daily, perSignalServices) = await volumeTask;
        var bytesBySignal = SignalBytes((await storageTask).Tables);

        var signals = new List<UsageSignalSummary>();
        var services = new List<UsageServiceRow>();
        foreach (var (signal, _, _) in Sources)
        {
            var byService = perSignalServices.GetValueOrDefault(signal) ?? [];
            var bytes = bytesBySignal.GetValueOrDefault(signal);
            signals.Add(new UsageSignalSummary(signal, byService.Values.Sum(), bytes));
            services.AddRange(UsageShaper.BuildServiceRows(signal, byService, bytes));
        }

        var (attributes, sampleRows) = await attributesTask;
        return new UsageResponse(
            timeProvider.GetUtcNow(), days, signals, daily, services,
            UsageShaper.TopAttributes(attributes), sampleRows, await keysTask);
    }

    private static Dictionary<string, long> SignalBytes(IReadOnlyList<TableStorageInfo> tables)
    {
        var bySignal = new Dictionary<string, long>();
        foreach (var table in tables)
        {
            var name = UsageShaper.NormalizeTableName(table.TableName);
            foreach (var (signal, sourceTables, _) in Sources)
            {
                if (sourceTables.Contains(name))
                {
                    bySignal[signal] = bySignal.GetValueOrDefault(signal) + table.CompressedBytes;
                }
            }
        }

        return bySignal;
    }

    private async Task<(List<UsageDayPoint> Daily, Dictionary<string, Dictionary<string, long>> ServicesBySignal)> ReadVolumeAsync(
        int days, CancellationToken cancellationToken)
    {
        var daily = new List<UsageDayPoint>();
        var services = new Dictionary<string, Dictionary<string, long>>();
        foreach (var (signal, tables, timeColumn) in Sources)
        {
            // `days` is clamped to an int by UsageShaper.ClampDays, so inlining it is injection-safe.
            var union = string.Join(
                "\nUNION ALL\n",
                tables.Select(t => $"SELECT toDate({timeColumn}) AS day, ServiceName, count() AS events FROM {t} WHERE {timeColumn} >= now() - INTERVAL {days} DAY GROUP BY day, ServiceName"));
            var sql = $"SELECT day, ServiceName, sum(events) FROM ({union}) GROUP BY day, ServiceName";

            var byService = new Dictionary<string, long>();
            var byDay = new Dictionary<DateTimeOffset, long>();
            await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
            while (reader.Read())
            {
                var day = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc));
                var service = reader.GetString(1);
                var events = (long)reader.GetFieldValue<ulong>(2);
                byService[service] = byService.GetValueOrDefault(service) + events;
                byDay[day] = byDay.GetValueOrDefault(day) + events;
            }

            services[signal] = byService;
            daily.AddRange(byDay.OrderBy(kv => kv.Key).Select(kv => new UsageDayPoint(kv.Key, signal, kv.Value)));
        }

        return (daily, services);
    }

    private async Task<(List<UsageAttributeRow> Rows, long SampleRows)> ReadAttributesAsync(CancellationToken cancellationToken)
    {
        var rows = new List<UsageAttributeRow>();
        foreach (var (signal, scope, table, column, timeColumn) in AttributeSources)
        {
            try
            {
                // Sample first (LIMIT inside), then explode the map: bounds the work to
                // AttributeSampleRows rows no matter how big the table is.
                var sql = $"""
                    SELECT k, sum(length(k) + length(v)) AS bytes, count() AS occurrences
                    FROM (SELECT {column} AS m FROM {table} WHERE {timeColumn} >= now() - INTERVAL 1 DAY LIMIT {AttributeSampleRows})
                    ARRAY JOIN mapKeys(m) AS k, mapValues(m) AS v
                    GROUP BY k
                    ORDER BY bytes DESC
                    LIMIT {UsageShaper.TopAttributesPerGroup}
                    """;
                await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
                while (reader.Read())
                {
                    rows.Add(new UsageAttributeRow(signal, scope, reader.GetString(0), (long)reader.GetFieldValue<ulong>(1), (long)reader.GetFieldValue<ulong>(2)));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Usage page's {Signal}/{Scope} attribute sample unavailable", signal, scope);
            }
        }

        return (rows, AttributeSampleRows);
    }

    private async Task<IReadOnlyList<UsageIngestKeyRow>> ReadIngestKeysAsync(CancellationToken cancellationToken)
    {
        var keys = (await ingestKeys.ListAsync(cancellationToken)).Where(k => k.IsActive).ToList();
        var usage = await ingestKeyUsage.GetUsageAsync(keys.Select(k => k.Id).ToList(), cancellationToken);
        return keys
            .Select(k => (Key: k, Usage: usage.GetValueOrDefault(k.Id)))
            .OrderByDescending(x => x.Usage.EventsToday)
            .ThenBy(x => x.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new UsageIngestKeyRow(x.Key.Id, x.Key.Name, x.Usage.EventsToday, x.Usage.BytesToday))
            .ToList();
    }

    private QueryOptions SafetyOptions() => QuerySafety.ExecutionTimeOnly(queryLimits.Value);
}
