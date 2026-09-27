using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using Flare.Api.Model;
using Flare.Identity.MetricMetadata;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IMetricCatalogQueryService
{
    Task<MetricCatalogResponse> ListAsync(MetricCatalogRequest request, CancellationToken cancellationToken);

    Task<MetricCatalogDetailResponse> GetDetailAsync(MetricCatalogDetailRequest request, CancellationToken cancellationToken);

    Task<MetricCatalogInspectResponse> InspectAsync(MetricCatalogInspectRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for the Metrics catalog - see
/// <see cref="MetricCatalogQueryBuilder"/> for the SQL. Its own service rather than more
/// methods on <see cref="IMetricQueryService"/>, which <see cref="Caching.CachingMetricQueryService"/>
/// decorates for the chart hot path this page isn't part of. Unit/description go through
/// <see cref="MetricMetadataOverlay"/> on the way out.
/// </summary>
public sealed class MetricCatalogQueryService(
    IClickHouseClient client,
    IMetricMetadataOverrideStore overrideStore,
    IOptions<QueryLimitsOptions> queryLimits,
    TimeProvider timeProvider) : IMetricCatalogQueryService
{
    public async Task<MetricCatalogResponse> ListAsync(MetricCatalogRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = MetricCatalogQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var built = MetricCatalogQueryBuilder.BuildCatalog(request, windowMinutes, timeProvider.GetUtcNow());

        var overrides = await overrideStore.GetAllAsync(cancellationToken);
        var metrics = new List<MetricCatalogEntry>();
        await using (var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                var metricName = reader.GetString(0);
                var metadataOverride = overrides.GetValueOrDefault(metricName);
                var (unit, description) = MetricMetadataOverlay.Apply(metadataOverride, NullIfEmpty(reader.GetString(2)), NullIfEmpty(reader.GetString(3)));
                metrics.Add(new MetricCatalogEntry
                {
                    MetricName = metricName,
                    Type = MetricQueryService.ParseType(reader.GetString(1)),
                    Unit = unit,
                    Description = description,
                    ServiceCount = ReadCount(reader, 4),
                    SeriesCount = ReadCount(reader, 5),
                    SampleCount = ReadCount(reader, 6),
                    LastReceivedUnixMs = ReadUnixMs(reader, 7),
                    HasMetadataOverride = metadataOverride is not null,
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

        var metadataOverride = (await overrideStore.GetAllAsync(cancellationToken)).GetValueOrDefault(request.MetricName);
        var (effectiveUnit, effectiveDescription) = MetricMetadataOverlay.Apply(metadataOverride, unit, description);

        return new MetricCatalogDetailResponse
        {
            MetricName = request.MetricName,
            Type = request.Type,
            Unit = effectiveUnit,
            Description = effectiveDescription,
            WindowMinutes = windowMinutes,
            Services = services,
            Attributes = attributes,
            Related = related,
            EmittedUnit = unit,
            EmittedDescription = description,
            HasMetadataOverride = metadataOverride is not null,
        };
    }

    public async Task<MetricCatalogInspectResponse> InspectAsync(MetricCatalogInspectRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = MetricInspectQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var bucketWidthSeconds = MetricInspectQueryBuilder.ClampBucketWidthSeconds(request.BucketWidthSeconds, windowMinutes);
        var now = timeProvider.GetUtcNow();

        // Rows arrive grouped by series, newest first within each - see BuildSamples.
        var raw = new List<MetricInspectRawSeries>();
        var samplesSql = MetricInspectQueryBuilder.BuildSamples(request, windowMinutes, now);
        await using (var reader = await client.ExecuteReaderAsync(samplesSql.Sql, samplesSql.Parameters, SafetyOptions(), cancellationToken))
        {
            string? serviceName = null;
            string? seriesKey = null;
            Dictionary<string, string>? attributes = null;
            var samples = new List<MetricInspectRawSample>();

            void Flush()
            {
                if (serviceName is null)
                {
                    return;
                }

                var truncated = samples.Count > MetricInspectQueryBuilder.MaxSamplesPerSeries;
                if (truncated)
                {
                    samples.RemoveAt(samples.Count - 1);
                }

                samples.Reverse();
                raw.Add(new MetricInspectRawSeries(serviceName, attributes!, samples, truncated));
                samples = [];
            }

            while (reader.Read())
            {
                var rowService = reader.GetString(0);
                var rowSeriesKey = reader.GetString(1);
                if (rowService != serviceName || rowSeriesKey != seriesKey)
                {
                    Flush();
                    serviceName = rowService;
                    seriesKey = rowSeriesKey;
                    attributes = reader.GetFieldValue<Dictionary<string, string>>(2);
                }

                samples.Add(new MetricInspectRawSample(
                    ReadUnixMs(reader, 3),
                    reader.GetDouble(4),
                    reader.GetFieldValue<byte>(5) != 0,
                    reader.GetFieldValue<byte>(6) != 0));
            }

            Flush();
        }

        long totalSeries = 0;
        var countSql = MetricInspectQueryBuilder.BuildSeriesCount(request, windowMinutes, now);
        await using (var reader = await client.ExecuteReaderAsync(countSql.Sql, countSql.Parameters, SafetyOptions(), cancellationToken))
        {
            if (reader.Read())
            {
                totalSeries = ReadCount(reader, 0);
            }
        }

        // Busiest series first, matching the query's pick order.
        raw.Sort((a, b) => b.Samples.Count.CompareTo(a.Samples.Count));
        var reduction = MetricInspectReducer.Reduce(request.Type, raw, bucketWidthSeconds);

        return new MetricCatalogInspectResponse
        {
            MetricName = request.MetricName,
            Type = request.Type,
            WindowMinutes = windowMinutes,
            BucketWidthSeconds = bucketWidthSeconds,
            TotalSeriesCount = Math.Max(totalSeries, raw.Count),
            Series = reduction.Series,
            Merged = reduction.Merged,
        };
    }


    private static long ReadCount(ClickHouseDataReader reader, int ordinal) => (long)reader.GetFieldValue<ulong>(ordinal);

    /// <summary>A <c>DateTime64</c> column as epoch ms - same UTC re-tagging rationale as <see cref="LogQueryService"/>'s own <c>ReadUtc</c>.</summary>
    private static long ReadUnixMs(ClickHouseDataReader reader, int ordinal) =>
        new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
