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
public sealed class RedisIngestionStatsTracker(
    IConnectionMultiplexer connectionMultiplexer,
    TimeProvider timeProvider) : IIngestionStatsTracker
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

        await Task.WhenAll(requests, records, bytes, expire).WaitAsync(cancellationToken);
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

        await Task.WhenAll(rejected, bucketExpire, push, trim, listExpire).WaitAsync(cancellationToken);
    }
}
