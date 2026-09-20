namespace Flare.Api.Caching;

/// <summary>Redis query-result cache tuning, bound from the <c>QueryCache</c> configuration section.</summary>
public sealed class QueryCacheOptions
{
    public const string SectionName = "QueryCache";

    /// <summary>Master on/off switch - false makes every caching decorator a pure pass-through, no Redis round trip at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How long a cached response stays valid before the next request re-runs the underlying ClickHouse query.</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>See <see cref="CacheRecencyGuard"/> - a query whose range ends within this long of "now" bypasses the cache entirely rather than risking a stale read of still-settling data.</summary>
    public TimeSpan RecentWindow { get; set; } = TimeSpan.FromMinutes(2);
}
