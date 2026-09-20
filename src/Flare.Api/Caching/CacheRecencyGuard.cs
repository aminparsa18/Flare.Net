namespace Flare.Api.Caching;

/// <summary>
/// Decides whether a query's time range is settled enough to cache - see the "Query
/// result caching" roadmap entry's cache-bypass requirement. Flare.Ingest buffers events
/// through Redis Streams before ClickHouse ever sees a row (ADR-0002), so a window whose
/// end still touches "now" can keep gaining rows for a little while after it's first
/// read; only a window that ends further in the past than the pipeline could plausibly
/// still be catching up on is safe to serve from cache.
/// </summary>
public static class CacheRecencyGuard
{
    /// <summary>
    /// True if a query whose range ends at <paramref name="to"/> (null means open-ended,
    /// i.e. "up to now") is safe to cache as of <paramref name="now"/>, given
    /// <paramref name="recentWindow"/> (see <see cref="QueryCacheOptions.RecentWindow"/>).
    /// </summary>
    public static bool IsCacheable(DateTimeOffset? to, DateTimeOffset now, TimeSpan recentWindow) =>
        to is { } t && t <= now - recentWindow;
}
