using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using Flare.Api.Model;

namespace Flare.Api.Query;

public interface IMetricQueryService
{
    Task<MetricNamesResponse> GetNamesAsync(MetricNamesRequest request, CancellationToken cancellationToken);

    Task<MetricQueryResponse> QueryAsync(MetricQueryRequest request, CancellationToken cancellationToken);

    Task<MetricAttributeKeysResponse> GetAttributeKeysAsync(MetricAttributeKeysRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for metrics - same role
/// as <see cref="LogQueryService"/>/<see cref="SpanQueryService"/>, deliberately its own
/// sibling class rather than a shared/generic one, same reasoning as those two.
/// </summary>
/// <remarks>
/// Same <c>ExecuteReaderAsync</c> + manual <see cref="ClickHouseDataReader"/> ordinal
/// reads as the other query services, not <c>QueryAsync&lt;T&gt;</c> - see
/// <see cref="LogQueryService"/>'s remarks for why.
/// </remarks>
public sealed class MetricQueryService(IClickHouseClient client, TimeProvider timeProvider) : IMetricQueryService
{
    public async Task<MetricNamesResponse> GetNamesAsync(MetricNamesRequest request, CancellationToken cancellationToken)
    {
        var built = MetricNamesQueryBuilder.Build(request, timeProvider.GetUtcNow());

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var metrics = new List<MetricNameInfo>();
        while (reader.Read())
        {
            metrics.Add(new MetricNameInfo
            {
                MetricName = reader.GetString(0),
                ServiceName = reader.GetString(1),
                Type = ParseType(reader.GetString(2)),
                Unit = NullIfEmpty(reader.GetString(3)),
                Description = NullIfEmpty(reader.GetString(4)),
                SeriesCount = (long)reader.GetFieldValue<ulong>(5),
            });
        }

        return new MetricNamesResponse { Metrics = metrics };
    }

    public async Task<MetricQueryResponse> QueryAsync(MetricQueryRequest request, CancellationToken cancellationToken)
    {
        var built = MetricSeriesQueryBuilder.Build(request, timeProvider.GetUtcNow());

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        // Rows arrive ordered by ServiceName, SeriesKey, BucketStart (see
        // MetricSeriesQueryBuilder) - fold consecutive rows sharing a
        // (ServiceName, SeriesKey) pair into one MetricSeries in a single pass, no
        // grouping dictionary needed.
        var series = new List<MetricSeries>();
        string? currentServiceName = null;
        string? currentSeriesKey = null;
        IReadOnlyDictionary<string, string>? currentAttributes = null;
        List<MetricSeriesPoint>? currentPoints = null;

        while (reader.Read())
        {
            var serviceName = reader.GetString(1);
            var seriesKey = reader.GetString(2);
            if (serviceName != currentServiceName || seriesKey != currentSeriesKey)
            {
                if (currentPoints is not null)
                {
                    series.Add(new MetricSeries { ServiceName = currentServiceName!, Attributes = currentAttributes!, Points = currentPoints });
                }

                currentServiceName = serviceName;
                currentSeriesKey = seriesKey;
                currentAttributes = reader.GetFieldValue<Dictionary<string, string>>(3);
                currentPoints = [];
            }

            currentPoints!.Add(ReadPoint(reader, built.Type));
        }

        if (currentPoints is not null)
        {
            series.Add(new MetricSeries { ServiceName = currentServiceName!, Attributes = currentAttributes!, Points = currentPoints });
        }

        return new MetricQueryResponse { Series = series };
    }

    public async Task<MetricAttributeKeysResponse> GetAttributeKeysAsync(MetricAttributeKeysRequest request, CancellationToken cancellationToken)
    {
        var built = MetricAttributeKeysQueryBuilder.Build(request, timeProvider.GetUtcNow());

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var keys = new List<MetricAttributeKeyInfo>();
        while (reader.Read())
        {
            keys.Add(new MetricAttributeKeyInfo
            {
                Key = reader.GetString(0),
                DistinctValueCount = (long)reader.GetFieldValue<ulong>(1),
            });
        }

        return new MetricAttributeKeysResponse { Keys = keys };
    }

    private static MetricSeriesPoint ReadPoint(ClickHouseDataReader reader, MetricPointType type)
    {
        var bucketStart = ReadUtc(reader, 0);

        if (type == MetricPointType.Histogram)
        {
            var count = reader.GetFieldValue<ulong>(4);
            var sum = reader.GetFieldValue<double>(5);
            var bucketCounts = reader.GetFieldValue<ulong[]>(6);
            var explicitBounds = reader.GetFieldValue<double[]>(7);

            return new MetricSeriesPoint
            {
                BucketStart = bucketStart,
                Count = (long)count,
                Sum = sum,
                P50 = HistogramQuantileEstimator.Estimate(bucketCounts, explicitBounds, 0.5),
                P75 = HistogramQuantileEstimator.Estimate(bucketCounts, explicitBounds, 0.75),
                P90 = HistogramQuantileEstimator.Estimate(bucketCounts, explicitBounds, 0.9),
                P95 = HistogramQuantileEstimator.Estimate(bucketCounts, explicitBounds, 0.95),
                P99 = HistogramQuantileEstimator.Estimate(bucketCounts, explicitBounds, 0.99),
                MaxApprox = HistogramQuantileEstimator.EstimateMax(bucketCounts, explicitBounds),
            };
        }

        if (type == MetricPointType.Sum)
        {
            return new MetricSeriesPoint
            {
                BucketStart = bucketStart,
                Value = reader.GetFieldValue<double>(4),
                Count = (long)reader.GetFieldValue<ulong>(5),
            };
        }

        return new MetricSeriesPoint { BucketStart = bucketStart, Value = reader.GetFieldValue<double>(4) };
    }

    /// <summary>Parses <see cref="MetricNamesQueryBuilder"/>'s literal <c>Type</c> discriminator back into the enum.</summary>
    private static MetricPointType ParseType(string type) => type switch
    {
        "gauge" => MetricPointType.Gauge,
        "sum" => MetricPointType.Sum,
        "histogram" => MetricPointType.Histogram,
        _ => throw new InvalidOperationException($"Unrecognized metric point type '{type}' returned by the query."),
    };

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>Same UTC re-tagging rationale as <see cref="LogQueryService"/>'s own <c>ReadUtc</c> - the ingest pipeline always writes UTC wall-clock values.</summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    /// <summary>Same scan/time safety cap as <see cref="LogQueryService.SafetyOptions"/>.</summary>
    private static QueryOptions SafetyOptions() => new()
    {
        CustomSettings = new Dictionary<string, object>
        {
            ["max_execution_time"] = 30,
            ["timeout_before_checking_execution_speed"] = 0,
            ["max_rows_to_read"] = 1_000_000_000,
            ["max_result_rows"] = 10_000,
            ["result_overflow_mode"] = "break",
        },
    };
}
