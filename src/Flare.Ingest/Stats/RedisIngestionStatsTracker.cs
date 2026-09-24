using System.Text.Json;
using StackExchange.Redis;

namespace Flare.Ingest.Stats;

/// <summary>
/// Redis-backed <see cref="IIngestionStatsTracker"/> - reuses the same
/// <c>IConnectionMultiplexer</c> the event sinks already hold (<see cref="Sinks.RedisStreamLogEventSink"/>
/// and friends), so this adds no new infrastructure dependency. One <c>IBatch</c> per call
/// (not sequential awaits) keeps this to a single Redis round trip per OTLP export request
/// regardless of how many counters/list ops it touches, since this runs on the hot ingest
/// path.
/// </summary>
/// <remarks>
/// Best-effort, per <see cref="IIngestionStatsTracker"/>'s own contract: a failed stats
/// write is logged and swallowed, never thrown. The receivers call this <em>after</em>
/// the events are already in the Redis stream, so an exception here used to turn a
/// successful export into a 500 that the exporter retried, duplicating every event in it
/// (seen with a HINCRBY overflow on the clock-skew counter - see <c>ClockSkew.Nanos</c>).
/// </remarks>
public sealed class RedisIngestionStatsTracker(
    IConnectionMultiplexer connectionMultiplexer,
    TimeProvider timeProvider,
    ILogger<RedisIngestionStatsTracker> logger) : IIngestionStatsTracker
{
    public async ValueTask RecordAcceptedAsync(
        IngestionSignal signal,
        IngestionProtocol protocol,
        int recordCount,
        long byteCount,
        CancellationToken cancellationToken = default)
    {
        var db = connectionMultiplexer.GetDatabase();
        var key = IngestionStatsKeys.MinuteBucketKey(timeProvider.GetUtcNow());
        var prefix = IngestionStatsKeys.FieldPrefix(signal, protocol);

        var batch = db.CreateBatch();
        var requests = batch.HashIncrementAsync(key, $"{prefix}:requests", 1);
        var records = batch.HashIncrementAsync(key, $"{prefix}:records", recordCount);
        var bytes = batch.HashIncrementAsync(key, $"{prefix}:bytes", byteCount);
        var expire = batch.KeyExpireAsync(key, IngestionStatsKeys.BucketTtl);
        batch.Execute();

        await BestEffortAsync(Task.WhenAll(requests, records, bytes, expire), "accepted counts", cancellationToken);
    }

    public async ValueTask RecordRejectedAsync(
        IngestionSignal signal,
        IngestionProtocol protocol,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var db = connectionMultiplexer.GetDatabase();
        var key = IngestionStatsKeys.MinuteBucketKey(now);
        var prefix = IngestionStatsKeys.FieldPrefix(signal, protocol);

        var entry = new IngestionErrorEntry(now, signal.ToString(), protocol.ToString(), reason);
        var payload = JsonSerializer.Serialize(entry, IngestionErrorEntryJsonContext.Default.IngestionErrorEntry);

        var batch = db.CreateBatch();
        var rejected = batch.HashIncrementAsync(key, $"{prefix}:rejected", 1);
        var bucketExpire = batch.KeyExpireAsync(key, IngestionStatsKeys.BucketTtl);
        var push = batch.ListLeftPushAsync(IngestionStatsKeys.ErrorsListKey, payload);
        var trim = batch.ListTrimAsync(IngestionStatsKeys.ErrorsListKey, 0, IngestionStatsKeys.MaxErrorEntries - 1);
        var listExpire = batch.KeyExpireAsync(IngestionStatsKeys.ErrorsListKey, IngestionStatsKeys.BucketTtl);
        batch.Execute();

        await BestEffortAsync(Task.WhenAll(rejected, bucketExpire, push, trim, listExpire), "rejection", cancellationToken);
    }

    public async ValueTask RecordServiceBreakdownAsync(
        IngestionSignal signal,
        IReadOnlyDictionary<string, ServiceAcceptedCounts> perService,
        CancellationToken cancellationToken = default)
    {
        if (perService.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var db = connectionMultiplexer.GetDatabase();
        var recordsKey = IngestionStatsKeys.ServiceRecordsKey(now, signal);
        var bytesKey = IngestionStatsKeys.ServiceBytesKey(now, signal);
        var skewKey = IngestionStatsKeys.ServiceSkewNanosKey(now, signal);

        var batch = db.CreateBatch();
        var tasks = new List<Task>(perService.Count * 3 + 3);
        foreach (var (service, counts) in perService)
        {
            tasks.Add(batch.HashIncrementAsync(recordsKey, service, counts.RecordCount));
            tasks.Add(batch.HashIncrementAsync(bytesKey, service, counts.ByteCount));
            tasks.Add(batch.HashIncrementAsync(skewKey, service, counts.SkewNanosSum));
        }

        tasks.Add(batch.KeyExpireAsync(recordsKey, IngestionStatsKeys.BucketTtl));
        tasks.Add(batch.KeyExpireAsync(bytesKey, IngestionStatsKeys.BucketTtl));
        tasks.Add(batch.KeyExpireAsync(skewKey, IngestionStatsKeys.BucketTtl));
        batch.Execute();

        await BestEffortAsync(Task.WhenAll(tasks), "per-service breakdown", cancellationToken);
    }

    private async ValueTask BestEffortAsync(Task writes, string what, CancellationToken cancellationToken)
    {
        try
        {
            await writes.WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to record ingestion stats ({What}); the export itself is unaffected.", what);
        }
    }
}
