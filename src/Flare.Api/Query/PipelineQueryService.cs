using Flare.Api.Model;
using StackExchange.Redis;

namespace Flare.Api.Query;

public interface IPipelineQueryService
{
    Task<PipelineStatsResponse> GetPipelineStatsAsync(PipelineStatsRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Backs the Ingestion page's pipeline-health section (Planning.md v10) - "is the buffered
/// pipeline keeping up", a different question than <see cref="IngestionStatsQueryService"/>
/// (v8's "is data arriving"). Three independent data sources, each degrading gracefully
/// rather than failing the whole response if unavailable (same "flag, don't fail wholesale"
/// precedent <see cref="IndexingQueryService"/> uses for <c>system.part_log</c>):
///
/// 1. <b>Stream/consumer-group health</b> - read *live* from Redis (<c>XLEN</c> +
///    <c>XINFO GROUPS</c> + <c>XPENDING</c>), not tracked in a hash the way the other two
///    are: Redis already holds this state natively, so writing a duplicate copy of it
///    would just be another thing that can drift. Live-verified against a real
///    <c>redis:latest</c> (8.10.0) container before committing to this shape - <c>XINFO
///    GROUPS</c>' own <c>lag</c> field (<see cref="StreamGroupInfo.Lag"/>) gives the true
///    "entries never yet delivered to this consumer group" count directly, a materially
///    better number than deriving it from <c>XLEN</c>/<c>XPENDING</c> math (that was the
///    originally planned approach - see Planning.md's design-decision note for how the
///    live probe changed it). Oldest-pending age comes from the extended <c>XPENDING</c>
///    form's own <c>IdleTimeInMilliseconds</c> on the single oldest entry (<c>count: 1</c> -
///    <c>XPENDING</c> iterates lowest-id-first) - the same command
///    <c>ClickHouseFlushWorker.ReclaimStalePendingAsync</c> already uses for reclaim, read
///    here instead of acted on. <see cref="PipelineStreamHealth.Capacity"/> is the one field
///    in this group *not* read live - it's each signal's configured MAXLEN
///    (<c>PipelineStreamKeys.Capacity</c>), a static number Redis doesn't expose a live
///    "current cap" query for, just passed straight through so the dashboard can show
///    length as a percentage of it.
/// 2. <b>Flush-worker health</b> - read back what
///    <c>Flare.Ingest.Stats.RedisFlushHealthTracker</c> (new, v10) writes once per flush
///    cycle.
/// 3. <b>Per-service.name breakdown</b> - read back what
///    <c>RedisIngestionStatsTracker.RecordServiceBreakdownAsync</c> (new, v10) writes per
///    accepted export request, summed across the requested window and capped to the top
///    <see cref="TopServicesPerSignal"/> services by record count (rest folded into
///    "other" - same "+N more" convention <c>MetricChart</c>/<c>IndexingGrowthChart</c>
///    already use for bounding a legend, not a silent drop).
/// </summary>
public sealed class PipelineQueryService(
    IConnectionMultiplexer connectionMultiplexer,
    TimeProvider timeProvider,
    PipelineStreamKeys streamKeys) : IPipelineQueryService
{
    private static readonly IngestionSignal[] Signals = Enum.GetValues<IngestionSignal>();

    /// <summary>Same 5-slot cap the <c>dataviz</c> skill's validated categorical palette uses elsewhere in this app.</summary>
    internal const int TopServicesPerSignal = 5;

    public async Task<PipelineStatsResponse> GetPipelineStatsAsync(PipelineStatsRequest request, CancellationToken cancellationToken = default)
    {
        var minutes = Math.Clamp(request.Minutes <= 0 ? 60 : request.Minutes, 1, 1440);
        var now = timeProvider.GetUtcNow();
        var db = connectionMultiplexer.GetDatabase();

        var streams = await Task.WhenAll(Signals.Select(s =>
            GetStreamHealthAsync(db, s, streamKeys.StreamKey(s), streamKeys.ConsumerGroup(s), streamKeys.Capacity(s), cancellationToken)));
        var flushWorkers = await Task.WhenAll(Signals.Select(s => GetFlushHealthAsync(db, s, cancellationToken)));
        var serviceBreakdowns = await Task.WhenAll(Signals.Select(s => GetServiceBreakdownAsync(db, s, now, minutes, cancellationToken)));

        return new PipelineStatsResponse(now, streams, flushWorkers, serviceBreakdowns);
    }

    private static async Task<PipelineStreamHealth> GetStreamHealthAsync(
        IDatabase db, IngestionSignal signal, string streamKey, string consumerGroup, long capacity, CancellationToken cancellationToken)
    {
        try
        {
            var length = await db.StreamLengthAsync(streamKey).WaitAsync(cancellationToken);
            var groups = await db.StreamGroupInfoAsync(streamKey).WaitAsync(cancellationToken);
            var group = groups.FirstOrDefault(g => string.Equals(g.Name, consumerGroup, StringComparison.Ordinal));

            if (group.Name is null)
            {
                // Stream exists (e.g. from a stale prior run) but this consumer group
                // doesn't - EnsureConsumerGroupAsync hasn't run yet on this stream.
                return new PipelineStreamHealth(signal, streamKey, Available: true, length, capacity, 0, 0, 0, null);
            }

            double? oldestPendingAgeSeconds = null;
            if (group.PendingMessageCount > 0)
            {
                var oldest = await db.StreamPendingMessagesAsync(streamKey, consumerGroup, 1, RedisValue.Null).WaitAsync(cancellationToken);
                if (oldest.Length > 0)
                {
                    oldestPendingAgeSeconds = oldest[0].IdleTimeInMilliseconds / 1000.0;
                }
            }

            return new PipelineStreamHealth(
                signal, streamKey, Available: true, length, capacity, group.Lag, group.PendingMessageCount, group.ConsumerCount, oldestPendingAgeSeconds);
        }
        catch (RedisServerException)
        {
            // Stream doesn't exist yet - no traffic on this signal since Flare.Ingest last
            // started (the group is only created, lazily, by EnsureConsumerGroupAsync on
            // that worker's first poll tick). Not an error state to surface as one.
            return new PipelineStreamHealth(signal, streamKey, Available: false, 0, capacity, 0, 0, 0, null);
        }
    }

    private static async Task<PipelineFlushHealth> GetFlushHealthAsync(IDatabase db, IngestionSignal signal, CancellationToken cancellationToken)
    {
        var hash = await db.HashGetAllAsync(FlushHealthKeys.HashKey(signal)).WaitAsync(cancellationToken);
        return BuildFlushHealth(signal, hash);
    }

    internal static PipelineFlushHealth BuildFlushHealth(IngestionSignal signal, HashEntry[] hash)
    {
        var fields = new Dictionary<string, string>(hash.Length);
        foreach (var entry in hash)
        {
            if (entry.Name.HasValue && entry.Value.HasValue)
            {
                fields[entry.Name!] = entry.Value!;
            }
        }

        return new PipelineFlushHealth(
            signal,
            LastFlushAt: TryParseUnixMilliseconds(fields, FlushHealthKeys.LastFlushAtField),
            LastBatchSize: TryParseLong(fields, FlushHealthKeys.LastBatchSizeField),
            LastErrorAt: TryParseUnixMilliseconds(fields, FlushHealthKeys.LastErrorAtField),
            LastError: fields.GetValueOrDefault(FlushHealthKeys.LastErrorField),
            ConsecutiveErrors: TryParseLong(fields, FlushHealthKeys.ConsecutiveErrorsField) ?? 0);
    }

    private static DateTimeOffset? TryParseUnixMilliseconds(IReadOnlyDictionary<string, string> fields, string field) =>
        fields.TryGetValue(field, out var raw) && long.TryParse(raw, out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;

    private static long? TryParseLong(IReadOnlyDictionary<string, string> fields, string field) =>
        fields.TryGetValue(field, out var raw) && long.TryParse(raw, out var value) ? value : null;

    private static async Task<PipelineServiceBreakdown> GetServiceBreakdownAsync(
        IDatabase db, IngestionSignal signal, DateTimeOffset now, int minutes, CancellationToken cancellationToken)
    {
        var nowMinute = now.ToUnixTimeSeconds() / 60;
        var startMinute = nowMinute - (minutes - 1);

        var batch = db.CreateBatch();
        var recordTasks = new Task<HashEntry[]>[minutes];
        var byteTasks = new Task<HashEntry[]>[minutes];
        for (var i = 0; i < minutes; i++)
        {
            var minute = startMinute + i;
            recordTasks[i] = batch.HashGetAllAsync(IngestionStatsKeys.ServiceRecordsKey(minute, signal));
            byteTasks[i] = batch.HashGetAllAsync(IngestionStatsKeys.ServiceBytesKey(minute, signal));
        }
        batch.Execute();

        await Task.WhenAll(recordTasks.Concat(byteTasks)).WaitAsync(cancellationToken);

        var recordsByService = SumHashes(recordTasks);
        var bytesByService = SumHashes(byteTasks);

        return BuildServiceBreakdown(signal, recordsByService, bytesByService);
    }

    private static Dictionary<string, long> SumHashes(Task<HashEntry[]>[] tasks)
    {
        var totals = new Dictionary<string, long>();
        foreach (var task in tasks)
        {
            foreach (var entry in task.Result)
            {
                if (entry.Name.HasValue && long.TryParse((string?)entry.Value, out var value))
                {
                    totals[entry.Name!] = totals.GetValueOrDefault(entry.Name!) + value;
                }
            }
        }

        return totals;
    }

    internal static PipelineServiceBreakdown BuildServiceBreakdown(
        IngestionSignal signal,
        IReadOnlyDictionary<string, long> recordsByService,
        IReadOnlyDictionary<string, long> bytesByService)
    {
        var ordered = recordsByService
            .Select(kv => new PipelineServiceEntry(kv.Key, kv.Value, bytesByService.GetValueOrDefault(kv.Key)))
            .OrderByDescending(e => e.Records)
            .ThenBy(e => e.ServiceName, StringComparer.Ordinal)
            .ToList();

        var top = ordered.Take(TopServicesPerSignal).ToList();
        var rest = ordered.Skip(TopServicesPerSignal).ToList();

        return new PipelineServiceBreakdown(
            signal,
            top,
            OtherServiceCount: rest.Count,
            OtherRecords: rest.Sum(e => e.Records),
            OtherBytes: rest.Sum(e => e.Bytes));
    }
}
