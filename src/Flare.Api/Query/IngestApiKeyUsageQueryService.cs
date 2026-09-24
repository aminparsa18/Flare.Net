using Flare.Identity.IngestKeys;
using StackExchange.Redis;

namespace Flare.Api.Query;

/// <summary>One key's accepted events/bytes in the current UTC minute and UTC day.</summary>
public readonly record struct IngestApiKeyUsage(long EventsThisMinute, long BytesThisMinute, long EventsToday, long BytesToday);

public interface IIngestApiKeyUsageQueryService
{
    Task<IReadOnlyDictionary<Guid, IngestApiKeyUsage>> GetUsageAsync(IReadOnlyList<Guid> keyIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the per-ingest-key usage counters <c>Flare.Ingest</c> writes (ADR-0051), via the
/// shared <see cref="IngestApiKeyUsageKeys"/> naming - so the Ingest Keys page shows exactly
/// the numbers the limits are enforced against. One <c>IBatch</c> round trip for every key
/// on the page.
/// </summary>
public sealed class IngestApiKeyUsageQueryService(IConnectionMultiplexer connectionMultiplexer, TimeProvider timeProvider) : IIngestApiKeyUsageQueryService
{
    private static readonly RedisValue[] Fields = [IngestApiKeyUsageKeys.EventsField, IngestApiKeyUsageKeys.BytesField];

    public async Task<IReadOnlyDictionary<Guid, IngestApiKeyUsage>> GetUsageAsync(IReadOnlyList<Guid> keyIds, CancellationToken cancellationToken = default)
    {
        if (keyIds.Count == 0)
        {
            return new Dictionary<Guid, IngestApiKeyUsage>();
        }

        var now = timeProvider.GetUtcNow();
        var db = connectionMultiplexer.GetDatabase();
        var batch = db.CreateBatch();
        var reads = keyIds
            .Select(id => (Id: id,
                Minute: batch.HashGetAsync(IngestApiKeyUsageKeys.MinuteKey(id, now), Fields),
                Day: batch.HashGetAsync(IngestApiKeyUsageKeys.DayKey(id, now), Fields)))
            .ToList();
        batch.Execute();

        await Task.WhenAll(reads.SelectMany(r => new[] { r.Minute, r.Day })).WaitAsync(cancellationToken);

        return reads.ToDictionary(
            r => r.Id,
            r => new IngestApiKeyUsage(
                EventsThisMinute: ToLong(r.Minute.Result[0]),
                BytesThisMinute: ToLong(r.Minute.Result[1]),
                EventsToday: ToLong(r.Day.Result[0]),
                BytesToday: ToLong(r.Day.Result[1])));
    }

    private static long ToLong(RedisValue value) => value.IsNull ? 0 : (long)value;
}
