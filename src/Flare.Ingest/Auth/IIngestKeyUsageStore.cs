using Flare.Identity.IngestKeys;
using StackExchange.Redis;

namespace Flare.Ingest.Auth;

/// <summary>One key's accepted events/bytes within one fixed window.</summary>
public readonly record struct IngestKeyWindowUsage(long Events, long Bytes);

/// <summary>A key's usage in the current UTC minute and the current UTC day.</summary>
public readonly record struct IngestKeyUsage(IngestKeyWindowUsage Minute, IngestKeyWindowUsage Day);

/// <summary>
/// Per-ingest-key usage counters (ADR-0051), shared across every <c>Flare.Ingest</c>
/// replica and readable by <c>Flare.Api</c> - which is why these live in Redis rather
/// than in-process, unlike <c>Flare.Api</c>'s own PAT rate limiter (ADR-0028):
/// <c>docker-compose.cluster.yml</c> runs two ingest replicas behind one load balancer,
/// and a per-process counter would let a key through at N times its configured cap.
/// </summary>
public interface IIngestKeyUsageStore
{
    ValueTask<IngestKeyUsage> GetAsync(Guid keyId, DateTimeOffset now, CancellationToken cancellationToken = default);

    ValueTask RecordAsync(Guid keyId, long events, long bytes, DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>Redis-backed <see cref="IIngestKeyUsageStore"/> - one <c>IBatch</c> round trip
/// per call, same discipline as <see cref="Stats.RedisIngestionStatsTracker"/> on the same
/// hot path.</summary>
public sealed class RedisIngestKeyUsageStore(IConnectionMultiplexer connectionMultiplexer) : IIngestKeyUsageStore
{
    private static readonly RedisValue[] Fields = [IngestApiKeyUsageKeys.EventsField, IngestApiKeyUsageKeys.BytesField];

    public async ValueTask<IngestKeyUsage> GetAsync(Guid keyId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var db = connectionMultiplexer.GetDatabase();
        var batch = db.CreateBatch();
        var minute = batch.HashGetAsync(IngestApiKeyUsageKeys.MinuteKey(keyId, now), Fields);
        var day = batch.HashGetAsync(IngestApiKeyUsageKeys.DayKey(keyId, now), Fields);
        batch.Execute();

        await Task.WhenAll(minute, day).WaitAsync(cancellationToken);
        return new IngestKeyUsage(ToUsage(minute.Result), ToUsage(day.Result));
    }

    public async ValueTask RecordAsync(Guid keyId, long events, long bytes, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var db = connectionMultiplexer.GetDatabase();
        var minuteKey = IngestApiKeyUsageKeys.MinuteKey(keyId, now);
        var dayKey = IngestApiKeyUsageKeys.DayKey(keyId, now);

        var batch = db.CreateBatch();
        Task[] tasks =
        [
            batch.HashIncrementAsync(minuteKey, IngestApiKeyUsageKeys.EventsField, events),
            batch.HashIncrementAsync(minuteKey, IngestApiKeyUsageKeys.BytesField, bytes),
            batch.KeyExpireAsync(minuteKey, IngestApiKeyUsageKeys.MinuteTtl),
            batch.HashIncrementAsync(dayKey, IngestApiKeyUsageKeys.EventsField, events),
            batch.HashIncrementAsync(dayKey, IngestApiKeyUsageKeys.BytesField, bytes),
            batch.KeyExpireAsync(dayKey, IngestApiKeyUsageKeys.DayTtl),
        ];
        batch.Execute();

        await Task.WhenAll(tasks).WaitAsync(cancellationToken);
    }

    private static IngestKeyWindowUsage ToUsage(RedisValue[] values) =>
        new(values[0].IsNull ? 0 : (long)values[0], values[1].IsNull ? 0 : (long)values[1]);
}
