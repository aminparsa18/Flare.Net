using Flare.Api.Model;
using Flare.Api.Query;
using Microsoft.Extensions.Options;

namespace Flare.Api.Caching;

/// <summary>
/// Wraps the real <see cref="MetricQueryService"/> with a short-TTL Redis cache in front
/// of <see cref="QueryAsync"/> - the metrics aggregate query path repeated dashboard-panel
/// refreshes hit verbatim (see the "Query result caching" roadmap entry). Name/
/// attribute-key discovery calls are left as plain pass-throughs: they back pickers, not
/// the repeated-poll hot path this exists for.
/// </summary>
public sealed class CachingMetricQueryService(
    IMetricQueryService inner,
    ICacheProvider cache,
    IOptions<QueryCacheOptions> options,
    TimeProvider timeProvider) : IMetricQueryService
{
    public Task<MetricNamesResponse> GetNamesAsync(MetricNamesRequest request, CancellationToken cancellationToken) =>
        inner.GetNamesAsync(request, cancellationToken);

    public Task<MetricQueryResponse> QueryAsync(MetricQueryRequest request, CancellationToken cancellationToken)
    {
        var cacheOptions = options.Value;
        if (!cacheOptions.Enabled || !CacheRecencyGuard.IsCacheable(request.Filter.To, timeProvider.GetUtcNow(), cacheOptions.RecentWindow))
        {
            return inner.QueryAsync(request, cancellationToken);
        }

        var key = QueryCacheKey.Build("metrics:query", request);
        return cache.GetOrCreateAsync(key, cacheOptions.Ttl, ct => inner.QueryAsync(request, ct), cancellationToken);
    }

    public Task<MetricAttributeKeysResponse> GetAttributeKeysAsync(MetricAttributeKeysRequest request, CancellationToken cancellationToken) =>
        inner.GetAttributeKeysAsync(request, cancellationToken);
}
