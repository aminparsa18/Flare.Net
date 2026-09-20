using MemoryPack;
using StackExchange.Redis;

namespace Flare.Api.Caching;

/// <summary>
/// <see cref="ICacheProvider"/> backed by the same Redis instance Flare.Ingest's stream
/// buffer, live-tail, and Data Protection key ring already depend on (see Program.cs's
/// <c>builder.AddRedisClient</c>) - no new infrastructure. Values are MemoryPack-encoded,
/// not JSON: every cached response type is already <c>[MemoryPackable]</c> for the
/// dashboard wire format (see <c>Json.ApiSerialization</c>), so this reuses that format
/// rather than paying for a second serializer.
/// </summary>
/// <remarks>
/// No stampede protection (a concurrent cache miss on the same key can run the factory
/// more than once) - the underlying ClickHouse queries this sits in front of already carry
/// their own execution caps (see each query service's <c>SafetyOptions</c>), and the
/// dashboard-panel/saved-search access pattern this exists for is one caller at a time
/// re-polling its own query, not a thundering herd on a single key.
/// </remarks>
public sealed class RedisCacheProvider(IConnectionMultiplexer connectionMultiplexer) : ICacheProvider
{
    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
    {
        var db = connectionMultiplexer.GetDatabase();

        var cached = await db.StringGetAsync(key).WaitAsync(cancellationToken);
        if (cached.HasValue)
        {
            return MemoryPackSerializer.Deserialize<T>((byte[])cached!)!;
        }

        var value = await factory(cancellationToken);
        await db.StringSetAsync(key, MemoryPackSerializer.Serialize(value), ttl).WaitAsync(cancellationToken);
        return value;
    }
}
