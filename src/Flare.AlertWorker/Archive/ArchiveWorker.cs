using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Export;
using Flare.Api.Model;
using Flare.Api.Query;
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
    ITelemetryExportQueryService settingsStore,
    IExportStatusStore statusStore,
    IConnectionMultiplexer redis,
    IOptions<ArchiveOptions> options,
    TimeProvider timeProvider,
    ILogger<ArchiveWorker> logger) : BackgroundService
{
    private static readonly RedisKey LockKey = "flare:archive:lock";
    private static readonly RedisKey WatermarkKey = "flare:archive:watermark";

    private readonly Dictionary<string, ArchiveTableStatus> tableStatus = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var (opts, source) = await ResolveAsync(stoppingToken);
                await ReportWorkerAsync(opts, source);
                if (opts is not null)
                {
                    var signals = opts.Signals.Count == 0 ? Enum.GetValues<ArchiveSignal>() : [.. opts.Signals];
                    await PollAsync(opts, signals.SelectMany(ArchiveSql.TablesFor).Distinct().ToList(), stoppingToken);
                }

                await Task.Delay(options.Value.PollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>Settings saved from the dashboard (ADR-0157) win over the <c>Archive</c> configuration; null options = the archive is off or incomplete.</summary>
    private async Task<(ArchiveOptions? Options, string Source)> ResolveAsync(CancellationToken ct)
    {
        var configured = options.Value;
        ArchiveSettings? stored = null;
        try
        {
            stored = await settingsStore.GetArchiveAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the saved archive settings; using configuration for this poll.");
        }

        var effective = stored is null
            ? configured
            : new ArchiveOptions
            {
                Enabled = stored.Enabled,
                Endpoint = stored.Endpoint,
                AccessKey = stored.AccessKey,
                SecretKey = stored.SecretKey,
                Prefix = stored.Prefix,
                Format = stored.Format == ArchiveFileFormat.Ndjson ? ArchiveFormat.Ndjson : ArchiveFormat.Parquet,
                Signals = stored.Signals.Select(x => (ArchiveSignal)(int)x).ToList(),
                PollInterval = configured.PollInterval,
                Lag = configured.Lag,
                StartFrom = configured.StartFrom,
                MaxWindowsPerPoll = configured.MaxWindowsPerPoll,
                MaxExecutionSeconds = configured.MaxExecutionSeconds,
            };
        var source = stored is null ? "config" : "settings";

        if (!effective.Enabled)
        {
            return (null, source);
        }

        if (effective.Validate() is { } problem)
        {
            logger.LogWarning("{Problem} The telemetry archive is idle until this is fixed.", problem);
            return (null, source);
        }

        return (effective, source);
    }

    private async Task ReportWorkerAsync(ArchiveOptions? opts, string source)
    {
        try
        {
            await statusStore.WriteArchiveWorkerAsync(new ArchiveWorkerState(opts is not null, source, timeProvider.GetUtcNow()));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not publish archive worker state.");
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
                var rows = await ExportAsync(opts, table, next, ct);
                await RecordAsync(table, previous => previous with
                {
                    LastExportedHour = next,
                    LastSuccessAt = timeProvider.GetUtcNow(),
                    LastRows = rows > 0 ? rows : previous.LastRows,
                    LastError = null,
                    LastErrorAt = null,
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Archiving {Table} for {Hour:u} failed; will retry.", table, next);
                await RecordAsync(table, previous => previous with
                {
                    LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message,
                    LastErrorAt = timeProvider.GetUtcNow(),
                });
                return;
            }

            next = next.AddHours(1);
            await db.HashSetAsync(WatermarkKey, table, next.ToUnixTimeSeconds());
        }
    }

    private async Task<long> ExportAsync(ArchiveOptions opts, string table, DateTimeOffset hour, CancellationToken ct)
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
            return 0;
        }

        var key = ArchiveSql.ObjectKey(opts.Prefix, table, hour, opts.Format);
        await client.ExecuteNonQueryAsync(
            ArchiveSql.ExportSql(opts.Endpoint, key, opts.AccessKey, opts.SecretKey, table, opts.Format),
            parameters, settings, ct);
        logger.LogInformation("Archived {Rows} row(s) of {Table} for {Hour:u} to {Key}.", rows, table, hour, key);
        return rows;
    }

    private async Task RecordAsync(string table, Func<ArchiveTableStatus, ArchiveTableStatus> change)
    {
        var status = change(tableStatus.GetValueOrDefault(table) ?? new ArchiveTableStatus { Table = table });
        tableStatus[table] = status;
        try
        {
            await statusStore.WriteArchiveTableAsync(status);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not publish archive status for {Table}.", table);
        }
    }
}
