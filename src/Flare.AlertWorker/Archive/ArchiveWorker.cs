using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.AlertWorker.Archive;

/// <summary>
/// Copies finished hours of telemetry to S3-compatible storage as Parquet or NDJSON (ADR-0156). ClickHouse does
/// the write itself (<c>INSERT INTO FUNCTION s3 ... SELECT</c>), so no rows pass through this process.
/// </summary>
/// <remarks>
/// Hours are cut by <c>IngestedAt</c>, not event time: a late or back-dated event is still archived in the hour
/// Flare received it, so a finished hour never changes. Progress is one watermark per table in Redis; losing it
/// only re-exports hours already written, because object keys are deterministic and the export overwrites. The
/// Redis lock keeps two worker replicas from exporting the same hour at once.
/// </remarks>
public sealed class ArchiveWorker(
    IClickHouseClient client,
    IConnectionMultiplexer redis,
    IOptions<ArchiveOptions> options,
    TimeProvider timeProvider,
    ILogger<ArchiveWorker> logger) : BackgroundService
{
    private static readonly RedisKey LockKey = "flare:archive:lock";
    private static readonly RedisKey WatermarkKey = "flare:archive:watermark";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("Telemetry archive is off (Archive:Enabled is false).");
            return;
        }

        if (opts.Validate() is { } problem)
        {
            logger.LogWarning("{Problem} The telemetry archive stays off.", problem);
            return;
        }

        var signals = opts.Signals.Count == 0 ? Enum.GetValues<ArchiveSignal>() : [.. opts.Signals];
        var tables = signals.SelectMany(ArchiveSql.TablesFor).Distinct().ToList();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await PollAsync(opts, tables, stoppingToken);
                await Task.Delay(opts.PollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task PollAsync(ArchiveOptions opts, IReadOnlyList<string> tables, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        RedisValue token = Guid.NewGuid().ToString("N");
        // Longer than a poll can take: MaxWindowsPerPoll exports per table, each capped by MaxExecutionSeconds.
        var lockTtl = TimeSpan.FromSeconds(opts.MaxExecutionSeconds * Math.Max(1, opts.MaxWindowsPerPoll) * tables.Count);
        if (!await db.LockTakeAsync(LockKey, token, lockTtl))
        {
            return;
        }

        try
        {
            foreach (var table in tables)
            {
                await ArchiveTableAsync(db, opts, table, ct);
            }
        }
        finally
        {
            await db.LockReleaseAsync(LockKey, token);
        }
    }

    private async Task ArchiveTableAsync(IDatabase db, ArchiveOptions opts, string table, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var stored = await db.HashGetAsync(WatermarkKey, table);
        var next = stored.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds((long)stored)
            : ArchiveSql.FloorToHour(opts.StartFrom ?? now - opts.Lag);

        for (var i = 0; i < opts.MaxWindowsPerPoll && next.AddHours(1) + opts.Lag <= now; i++)
        {
            try
            {
                await ExportAsync(opts, table, next, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Archiving {Table} for {Hour:u} failed; will retry.", table, next);
                return;
            }

            next = next.AddHours(1);
            await db.HashSetAsync(WatermarkKey, table, next.ToUnixTimeSeconds());
        }
    }

    private async Task ExportAsync(ArchiveOptions opts, string table, DateTimeOffset hour, CancellationToken ct)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("from", hour.UtcDateTime);
        parameters.AddParameter("to", hour.AddHours(1).UtcDateTime);

        var settings = new QueryOptions
        {
            CustomSettings = new Dictionary<string, object> { ["max_execution_time"] = opts.MaxExecutionSeconds },
        };

        var rows = Convert.ToInt64(await client.ExecuteScalarAsync(ArchiveSql.CountSql(table), parameters, settings, ct));
        if (rows == 0)
        {
            return;
        }

        var key = ArchiveSql.ObjectKey(opts.Prefix, table, hour, opts.Format);
        await client.ExecuteNonQueryAsync(
            ArchiveSql.ExportSql(opts.Endpoint, key, opts.AccessKey, opts.SecretKey, table, opts.Format),
            parameters, settings, ct);
        logger.LogInformation("Archived {Rows} row(s) of {Table} for {Hour:u} to {Key}.", rows, table, hour, key);
    }
}
