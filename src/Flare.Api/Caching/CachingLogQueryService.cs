using Flare.Api.Model;
using Flare.Api.Query;
using Microsoft.Extensions.Options;

namespace Flare.Api.Caching;

/// <summary>
/// Wraps the real <see cref="LogQueryService"/> with a short-TTL Redis cache in front of
/// <see cref="SearchAsync"/>/<see cref="AggregateAsync"/> - the two calls dashboard-panel
/// refreshes and saved-search reruns repeat verbatim (see the "Query result caching"
/// roadmap entry). Every other member is a plain pass-through: pattern/attribute/
/// value-distribution/active-service/context lookups and the free-form SQL-query-row
/// surface aren't the repeated-poll hot path this exists for.
/// </summary>
public sealed class CachingLogQueryService(
    ILogQueryService inner,
    ICacheProvider cache,
    IOptions<QueryCacheOptions> options,
    TimeProvider timeProvider) : ILogQueryService
{
    public Task<LogSearchResponse> SearchAsync(LogSearchRequest request, CancellationToken cancellationToken) =>
        MaybeCachedAsync("logs:search", request, request.Filter.To, ct => inner.SearchAsync(request, ct), cancellationToken);

    public Task<LogAggregateResponse> AggregateAsync(LogAggregateRequest request, CancellationToken cancellationToken) =>
        MaybeCachedAsync("logs:aggregate", request, request.Filter.To, ct => inner.AggregateAsync(request, ct), cancellationToken);

    public Task<LogQlQueryResponse> RunQlQueryAsync(LogQlQueryRequest request, CancellationToken cancellationToken) =>
        inner.RunQlQueryAsync(request, cancellationToken);

    public Task<LogContextResponse> GetContextAsync(LogContextRequest request, CancellationToken cancellationToken) =>
        inner.GetContextAsync(request, cancellationToken);

    public Task<LogPatternResponse> GetPatternsAsync(LogPatternRequest request, CancellationToken cancellationToken) =>
        inner.GetPatternsAsync(request, cancellationToken);

    public Task<LogAttributeKeysResponse> GetNumericAttributeKeysAsync(LogAttributeKeysRequest request, CancellationToken cancellationToken) =>
        inner.GetNumericAttributeKeysAsync(request, cancellationToken);

    public Task<LogValueDistributionResponse> GetValueDistributionAsync(LogValueDistributionRequest request, CancellationToken cancellationToken) =>
        inner.GetValueDistributionAsync(request, cancellationToken);

    public Task<LogAttributeValuesResponse> GetAttributeValuesAsync(LogAttributeValuesRequest request, CancellationToken cancellationToken) =>
        inner.GetAttributeValuesAsync(request, cancellationToken);

    public Task<IReadOnlyList<ActiveService>> GetActiveServiceNamesAsync(TimeSpan window, CancellationToken cancellationToken) =>
        inner.GetActiveServiceNamesAsync(window, cancellationToken);

    private Task<TResponse> MaybeCachedAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        DateTimeOffset? filterTo,
        Func<CancellationToken, Task<TResponse>> factory,
        CancellationToken cancellationToken)
    {
        var cacheOptions = options.Value;
        if (!cacheOptions.Enabled || !CacheRecencyGuard.IsCacheable(filterTo, timeProvider.GetUtcNow(), cacheOptions.RecentWindow))
        {
            return factory(cancellationToken);
        }

        var key = QueryCacheKey.Build(endpoint, request);
        return cache.GetOrCreateAsync(key, cacheOptions.Ttl, factory, cancellationToken);
    }
}
