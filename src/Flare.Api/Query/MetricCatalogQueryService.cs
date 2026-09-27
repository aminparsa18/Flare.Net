using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IMetricCatalogQueryService
{
    Task<MetricCatalogResponse> ListAsync(MetricCatalogRequest request, CancellationToken cancellationToken);

    Task<MetricCatalogDetailResponse> GetDetailAsync(MetricCatalogDetailRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for the Metrics catalog - see
/// <see cref="MetricCatalogQueryBuilder"/> for the SQL. Its own service rather than more
/// methods on <see cref="IMetricQueryService"/>, which <see cref="Caching.CachingMetricQueryService"/>
/// decorates for the chart hot path this page isn't part of.
/// </summary>
public sealed class MetricCatalogQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IMetricCatalogQueryService
{
    public async Task<MetricCatalogResponse> ListAsync(MetricCatalogRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = MetricCatalogQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var built = MetricCatalogQueryBuilder.BuildCatalog(request, windowMinutes, timeProvider.GetUtcNow());

        var metrics = new List<MetricCatalogEntry>();
        await using (var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                metrics.Add(new MetricCatalogEntry
                {
                    MetricName = reader.GetString(0),
                    Type = MetricQueryService.ParseType(reader.GetString(1)),
                    Unit = NullIfEmpty(reader.GetString(2)),
                    Description = NullIfEmpty(reader.GetString(3)),
                    ServiceCount = ReadCount(reader, 4),
                    SeriesCount = ReadCount(reader, 5),
                    SampleCount = ReadCount(reader, 6),
                    LastReceivedUnixMs = ReadUnixMs(reader, 7),
                });
            }
        }

        var truncated = metrics.Count > MetricCatalogQueryBuilder.MaxMetrics;
        if (truncated)
        {
            metrics.RemoveRange(MetricCatalogQueryBuilder.MaxMetrics, metrics.Count - MetricCatalogQueryBuilder.MaxMetrics);
        }

        return new MetricCatalogResponse { WindowMinutes = windowMinutes, Metrics = metrics, Truncated = truncated };
    }

    public async Task<MetricCatalogDetailResponse> GetDetailAsync(MetricCatalogDetailRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = MetricCatalogQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var now = timeProvider.GetUtcNow();

        string? unit = null;
        string? description = null;
        var services = new List<MetricCatalogServiceInfo>();
        var servicesSql = MetricCatalogQueryBuilder.BuildServices(request, windowMinutes, now);
        await using (var reader = await client.ExecuteReaderAsync(servicesSql.Sql, servicesSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                unit ??= NullIfEmpty(reader.GetString(1));
                description ??= NullIfEmpty(reader.GetString(2));
                services.Add(new MetricCatalogServiceInfo
                {
                    ServiceName = reader.GetString(0),
                    SeriesCount = ReadCount(reader, 3),
                    SampleCount = ReadCount(reader, 4),
                    LastReceivedUnixMs = ReadUnixMs(reader, 5),
                });
            }
        }

        var attributes = new List<MetricCatalogAttributeInfo>();
        var attributesSql = MetricCatalogQueryBuilder.BuildAttributes(request, windowMinutes, now);
        await using (var reader = await client.ExecuteReaderAsync(attributesSql.Sql, attributesSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                attributes.Add(new MetricCatalogAttributeInfo
                {
                    Key = reader.GetString(0),
                    DistinctValueCount = ReadCount(reader, 1),
                    SampleCount = ReadCount(reader, 2),
                    SampleValues = reader.GetFieldValue<string[]>(3),
                });
            }
        }

        // No point ranking related metrics for a metric with no data in the window - the
        // detail view shows its empty state instead.
        IReadOnlyList<MetricCatalogRelatedMetric> related = [];
        if (services.Count > 0)
        {
            var candidates = new List<MetricRelationCandidate>();
            var relatedSql = MetricCatalogQueryBuilder.BuildRelatedCandidates(windowMinutes, now);
            await using (var reader = await client.ExecuteReaderAsync(relatedSql.Sql, relatedSql.Parameters, SafetyOptions(), cancellationToken))
            {
                while (reader.Read())
                {
                    candidates.Add(new MetricRelationCandidate(
                        reader.GetString(0),
                        MetricQueryService.ParseType(reader.GetString(1)),
                        reader.GetFieldValue<string[]>(2),
                        reader.GetFieldValue<string[]>(3)));
                }
            }

            // The target's own row carries its full key set; fall back to what the attribute
            // query found if the candidate cap cut it off.
            var target = candidates.Find(c => c.MetricName == request.MetricName && c.Type == request.Type)
                ?? new MetricRelationCandidate(request.MetricName, request.Type, services.ConvertAll(s => s.ServiceName), attributes.ConvertAll(a => a.Key));
            related = MetricRelatedRanker.Rank(target, candidates, MetricCatalogQueryBuilder.MaxRelated);
        }

        return new MetricCatalogDetailResponse
        {
            MetricName = request.MetricName,
            Type = request.Type,
            Unit = unit,
            Description = description,
            WindowMinutes = windowMinutes,
            Services = services,
            Attributes = attributes,
            Related = related,
        };
    }

    private static long ReadCount(ClickHouseDataReader reader, int ordinal) => (long)reader.GetFieldValue<ulong>(ordinal);

    /// <summary>A <c>DateTime64</c> column as epoch ms - same UTC re-tagging rationale as <see cref="LogQueryService"/>'s own <c>ReadUtc</c>.</summary>
    private static long ReadUnixMs(ClickHouseDataReader reader, int ordinal) =>
        new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
