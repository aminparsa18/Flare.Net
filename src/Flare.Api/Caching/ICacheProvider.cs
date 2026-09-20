namespace Flare.Api.Caching;

/// <summary>
/// The caching seam behind Flare.Api's read-heavy query services - see the "Query result
/// caching" roadmap entry. A thin interface (rather than callers touching
/// <c>IConnectionMultiplexer</c> directly) so a query service's own tests, if any are ever
/// added, can substitute an in-memory fake instead of a real Redis instance.
/// </summary>
public interface ICacheProvider
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/> if present; otherwise runs
    /// <paramref name="factory"/>, caches the result for <paramref name="ttl"/>, and
    /// returns it.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken);
}
