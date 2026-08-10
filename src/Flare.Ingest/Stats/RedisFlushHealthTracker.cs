using StackExchange.Redis;

namespace Flare.Ingest.Stats;

/// <summary>
/// Redis-backed <see cref="IFlushHealthTracker"/> - same shared-<c>IConnectionMultiplexer</c>/
/// single-<c>IBatch</c>-round-trip approach as <see cref="RedisIngestionStatsTracker"/>.
/// </summary>
public sealed class RedisFlushHealthTracker(
    IConnectionMultiplexer connectionMultiplexer,
    TimeProvider timeProvider) : IFlushHealthTracker
{
    public async ValueTask RecordSuccessAsync(
        IngestionSignal signal,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var db = connectionMultiplexer.GetDatabase();
        var key = FlushHealthKeys.HashKey(signal);
        var now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

        var batch = db.CreateBatch();
        var flushed = batch.HashSetAsync(key,
        [
            new HashEntry(FlushHealthKeys.LastFlushAtField, now),
            new HashEntry(FlushHealthKeys.LastBatchSizeField, batchSize),
            new HashEntry(FlushHealthKeys.ConsecutiveErrorsField, 0),
        ]);
        batch.Execute();

        await flushed.WaitAsync(cancellationToken);
    }

    public async ValueTask RecordFailureAsync(
        IngestionSignal signal,
        string error,
        CancellationToken cancellationToken = default)
    {
        var db = connectionMultiplexer.GetDatabase();
        var key = FlushHealthKeys.HashKey(signal);
        var now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

        var batch = db.CreateBatch();
        var set = batch.HashSetAsync(key,
        [
            new HashEntry(FlushHealthKeys.LastErrorAtField, now),
            new HashEntry(FlushHealthKeys.LastErrorField, error),
        ]);
        var incremented = batch.HashIncrementAsync(key, FlushHealthKeys.ConsecutiveErrorsField, 1);
        batch.Execute();

        await Task.WhenAll(set, incremented).WaitAsync(cancellationToken);
    }
}
