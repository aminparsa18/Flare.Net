using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT ... GROUP BY</c> for <c>POST /api/spans/attribute-values</c>, ready to hand to <see cref="SpanQueryService"/>.</summary>
public sealed record SpanAttributeValuesSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="SpanAttributeValuesRequest"/> → parameterized SQL builder - the Traces
/// page's equivalent of <see cref="LogAttributeValuesQueryBuilder"/>, same shape (no
/// subquery needed - see that builder's remarks), not a shared/reused one, same
/// keep-logs-and-spans-independent reasoning <see cref="SpanFilterSqlBuilder"/> gives for
/// not reusing <see cref="LogFilterSqlBuilder"/>.
/// </summary>
public static class SpanAttributeValuesQueryBuilder
{
    /// <summary>
    /// Inclusive lower bounds (ns) of the <see cref="SpanValuesField.DurationBucket"/>
    /// buckets, ascending - each bucket runs up to (excluding) the next bound, the last is
    /// open-ended. Mirrored by the dashboard's <c>$lib/traces/duration-buckets.ts</c>, which
    /// turns a bucket back into <see cref="SpanFilter.MinDurationNano"/>/<see cref="SpanFilter.MaxDurationNano"/>.
    /// </summary>
    public static readonly IReadOnlyList<ulong> DurationBucketLowerBoundsNano =
    [
        0,
        1_000_000, // 1ms
        10_000_000, // 10ms
        100_000_000, // 100ms
        500_000_000, // 500ms
        1_000_000_000, // 1s
        5_000_000_000, // 5s
    ];

    public static SpanAttributeValuesSql Build(SpanAttributeValuesRequest request, DateTimeOffset now)
    {
        if (request.Field == SpanValuesField.Attribute && string.IsNullOrEmpty(request.Key))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Key, "Key must be non-empty.");
        }

        if (request.Limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Limit, "Limit must be positive.");
        }

        var filterSql = SpanFilterSqlBuilder.Build(request.Filter ?? new SpanFilter(), now);
        filterSql.Parameters.AddParameter("valuesLimit", request.Limit);

        var whereClauses = new List<string> { filterSql.WhereSql };
        string valueSql;
        switch (request.Field)
        {
            case SpanValuesField.Service:
                valueSql = "ServiceName";
                break;
            case SpanValuesField.Status:
                valueSql = "toString(StatusCode)";
                break;
            case SpanValuesField.Kind:
                valueSql = "toString(Kind)";
                break;
            case SpanValuesField.Name:
                valueSql = "Name";
                break;
            case SpanValuesField.DurationBucket:
                valueSql = DurationBucketSql();
                break;
            default:
                var column = SpanFilterSqlBuilder.ColumnFor(request.Bag);
                filterSql.Parameters.AddParameter("valuesKey", request.Key);
                valueSql = $"{column}[{{valuesKey:String}}]";
                whereClauses.Add($"mapContains({column}, {{valuesKey:String}})");
                break;
        }

        if (!string.IsNullOrEmpty(request.Prefix))
        {
            filterSql.Parameters.AddParameter("valuesPrefix", LogFilterSqlBuilder.ContainsPattern(request.Prefix));
            whereClauses.Add($"{valueSql} ILIKE {{valuesPrefix:String}}");
        }

        var sql = $"SELECT {valueSql} AS Value, count() AS Cnt\n" +
            "FROM spans\n" +
            $"WHERE {string.Join(" AND ", whereClauses)}\n" +
            "GROUP BY Value\n" +
            "ORDER BY Cnt DESC\n" +
            "LIMIT {valuesLimit:UInt32}";

        return new SpanAttributeValuesSql(sql, filterSql.Parameters);
    }

    /// <summary>
    /// <c>toString(multiIf(DurationNano &lt; b1, b0, DurationNano &lt; b2, b1, ..., bN))</c> over
    /// <see cref="DurationBucketLowerBoundsNano"/> - the bounds are compile-time constants,
    /// never request text, so inlining them is safe.
    /// </summary>
    private static string DurationBucketSql()
    {
        var bounds = DurationBucketLowerBoundsNano;
        var arms = new List<string>();
        for (var i = 1; i < bounds.Count; i++)
        {
            arms.Add($"DurationNano < {bounds[i]}, {bounds[i - 1]}");
        }

        return $"toString(multiIf({string.Join(", ", arms)}, {bounds[^1]}))";
    }
}
