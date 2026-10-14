using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Which attribute names one bag of spans carries in a window - the Tempo-compatible API's tag discovery.</summary>
public sealed record SpanAttributeKeysRequest(SpanAttributeBag Bag, DateTimeOffset From, DateTimeOffset To, int Limit = SpanAttributeKeysQueryBuilder.DefaultLimit);

/// <summary>Fully-built <c>SELECT</c> for <see cref="ISpanQueryService.GetAttributeKeysAsync"/>.</summary>
public sealed record SpanAttributeKeysSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="SpanAttributeKeysRequest"/> → parameterized SQL: the most common attribute keys of
/// one bag among a sample of the window's spans. Pure, no ClickHouse dependency.
/// </summary>
/// <remarks>
/// <c>arrayJoin(mapKeys(...))</c> multiplies rows by the size of each map, so it runs over at most
/// <see cref="SampleSpans"/> spans of the window rather than all of them. A key carried by a tiny
/// fraction of a very large window can therefore be missing; the result is discovery for a query
/// builder's autocomplete, not an inventory.
/// </remarks>
public static class SpanAttributeKeysQueryBuilder
{
    public const int DefaultLimit = 200;

    public const int MaxLimit = 1000;

    public const int SampleSpans = 50_000;

    public static SpanAttributeKeysSql Build(SpanAttributeKeysRequest request, DateTimeOffset now, PromotedAttributeColumns? promoted = null)
    {
        if (request.Limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Limit, "Limit must be positive.");
        }

        var filterSql = SpanFilterSqlBuilder.Build(new SpanFilter { From = request.From, To = request.To }, now, promoted);
        filterSql.Parameters.AddParameter("keysLimit", Math.Min(request.Limit, MaxLimit));
        filterSql.Parameters.AddParameter("keysSample", SampleSpans);

        var column = SpanFilterSqlBuilder.ColumnFor(request.Bag);
        var sql = $"SELECT arrayJoin(mapKeys(Attributes)) AS AttributeKey, count() AS Cnt\n" +
            $"FROM (SELECT {column} AS Attributes FROM spans WHERE {filterSql.WhereSql} LIMIT {{keysSample:UInt32}})\n" +
            "GROUP BY AttributeKey\n" +
            "ORDER BY Cnt DESC, AttributeKey\n" +
            "LIMIT {keysLimit:UInt32}";

        return new SpanAttributeKeysSql(sql, filterSql.Parameters);
    }
}
