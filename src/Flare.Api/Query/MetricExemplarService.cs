using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

/// <summary>Which metric's exemplars to read, within a <see cref="MetricFilter"/> (time range, services, data-point attribute equality).</summary>
public sealed class MetricExemplarsRequest
{
    public required string MetricName { get; init; }

    public required MetricPointType Type { get; init; }

    public MetricFilter Filter { get; init; } = new();

    /// <summary>Newest first; clamped to <see cref="MetricExemplarQueryBuilder.MaxLimit"/>.</summary>
    public int Limit { get; init; } = MetricExemplarQueryBuilder.DefaultLimit;
}

/// <summary>One stored exemplar with the identity of the point it was recorded on.</summary>
public sealed record MetricExemplarRow(
    string ServiceName,
    IReadOnlyDictionary<string, string> DataPointAttributes,
    DateTimeOffset Time,
    double Value,
    string TraceId,
    string? SpanId,
    IReadOnlyDictionary<string, string> FilteredAttributes);

public interface IMetricExemplarService
{
    Task<IReadOnlyList<MetricExemplarRow>> GetAsync(MetricExemplarsRequest request, CancellationToken cancellationToken);
}

/// <summary>Fully-built <c>SELECT</c> over <c>metric_exemplars</c> (ADR-0241).</summary>
public sealed record MetricExemplarSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="MetricExemplarsRequest"/> → parameterized SQL. The metric filter is the same
/// <see cref="MetricFilterSqlBuilder"/> fragment the series queries use (the exemplar table carries the same
/// <c>Time</c>, <c>ServiceName</c> and <c>DataPointAttributes</c> columns), so a selector selects the same
/// points whether it is read as a series or as exemplars.
/// </summary>
public static class MetricExemplarQueryBuilder
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 1000;

    public static MetricExemplarSql Build(MetricExemplarsRequest request, DateTimeOffset now)
    {
        var filterSql = MetricFilterSqlBuilder.Build(request.Filter, now);
        filterSql.Parameters.AddParameter("metricName", request.MetricName);
        filterSql.Parameters.AddParameter("pointType", PointType(request.Type));
        filterSql.Parameters.AddParameter("limit", (ulong)Math.Clamp(request.Limit, 1, MaxLimit));

        var sql = "SELECT ServiceName, DataPointAttributes, Time, Value, TraceId, SpanId, FilteredAttributes\n" +
            "FROM metric_exemplars\n" +
            $"WHERE MetricName = {{metricName:String}} AND PointType = {{pointType:String}} AND {filterSql.WhereSql}\n" +
            "ORDER BY Time DESC\n" +
            "LIMIT {limit:UInt64}";
        return new MetricExemplarSql(sql, filterSql.Parameters);
    }

    /// <summary>The <c>PointType</c> label the ingest writer stores (migration 0086).</summary>
    public static string PointType(MetricPointType type) => type switch
    {
        MetricPointType.Gauge => "gauge",
        MetricPointType.Sum => "sum",
        MetricPointType.Histogram => "histogram",
        MetricPointType.ExponentialHistogram => "exponential_histogram",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown metric point type."),
    };
}

/// <summary>The one component reading <c>metric_exemplars</c>; same reader-ordinal style and scan caps as <see cref="MetricQueryService"/>.</summary>
public sealed class MetricExemplarService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IMetricExemplarService
{
    public async Task<IReadOnlyList<MetricExemplarRow>> GetAsync(MetricExemplarsRequest request, CancellationToken cancellationToken)
    {
        var built = MetricExemplarQueryBuilder.Build(request, timeProvider.GetUtcNow());
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);

        var rows = new List<MetricExemplarRow>();
        while (reader.Read())
        {
            var spanId = reader.GetString(5);
            rows.Add(new MetricExemplarRow(
                reader.GetString(0),
                reader.GetFieldValue<Dictionary<string, string>>(1),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc)),
                reader.GetDouble(3),
                reader.GetString(4),
                spanId.Length == 0 ? null : spanId,
                reader.GetFieldValue<Dictionary<string, string>>(6)));
        }

        return rows;
    }
}
