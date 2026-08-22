using ClickHouse.Driver.ADO.Parameters;
using Flare.Api.Model;

namespace Flare.Api.Query.LogQl;

/// <summary>Which shape <see cref="LogQlQueryBuilder.Build"/> dispatched to - drives how <c>LogQueryService.RunQlQueryAsync</c> reads the result set back.</summary>
public enum LogQlDispatchKind
{
    Count,
    Series,
    Rows,
}

/// <summary>Fully-built SQL for one SQL-query-row request, ready for <see cref="LogQueryService"/>.</summary>
public sealed record LogQlBuiltQuery(LogQlDispatchKind Kind, string Sql, ClickHouseParameterCollection Parameters, bool HasGroupKey);

/// <summary>
/// Orchestrates one SQL-query-row request: parse the query text
/// (<see cref="LogQlParser"/>), combine the time-bound base filter with the parsed
/// <c>where</c> clause (<see cref="LogQlWhereTranslator"/>), then dispatch to whichever
/// existing, already-tested builder matches the parsed shape - reuses
/// <see cref="LogAggregateQueryBuilder.BuildFromFilterSql"/>/<see cref="LogSearchQueryBuilder.BuildFromFilterSql"/>
/// rather than duplicating their bucketing/paging SQL. No ClickHouse dependency, same
/// "pure function" style as every other builder in this namespace.
/// </summary>
public static class LogQlQueryBuilder
{
    /// <summary>
    /// Raw rows (<c>select * ...</c>) are capped at this many events - no
    /// cursor/pagination in v1, per this feature's plan doc. "I need more than this" is
    /// answered by narrowing the query/time range, not by paging through a query-bar result.
    /// </summary>
    public const int RawRowLimit = 100;

    public static LogQlBuiltQuery Build(LogQlQueryRequest request, DateTimeOffset now)
    {
        var query = LogQlParser.Parse(request.Query);

        // Only the time bound comes from the structured filter - the toolbar's
        // service/severity/search filters are deliberately not ANDed in (see this
        // feature's plan doc: "once you're writing WHERE yourself, that's the source of
        // truth").
        var filterSql = LogFilterSqlBuilder.Build(new LogFilter { From = request.From, To = request.To }, now);

        if (query.Where is not null)
        {
            var whereFragment = LogQlWhereTranslator.Translate(query.Where, filterSql.Parameters);
            filterSql = filterSql with { WhereSql = $"{filterSql.WhereSql} AND {whereFragment}" };
        }

        if (query.GroupBy is { } groupBy)
        {
            // Parser already rejected GroupBy paired with Raw - see LogQlParser.Parse's
            // trailing validation.
            var aggregate = LogAggregateQueryBuilder.BuildFromFilterSql(filterSql, groupBy.TimeBucketSeconds, groupBy.Secondary);
            return new LogQlBuiltQuery(LogQlDispatchKind.Series, aggregate.Sql, aggregate.Parameters, aggregate.HasGroupKey);
        }

        if (query.Select == LogQlSelectKind.Count)
        {
            var sql = $"SELECT count() FROM logs WHERE {filterSql.WhereSql}";
            return new LogQlBuiltQuery(LogQlDispatchKind.Count, sql, filterSql.Parameters, HasGroupKey: false);
        }

        var search = LogSearchQueryBuilder.BuildFromFilterSql(filterSql, cursor: null, pageSize: RawRowLimit);
        return new LogQlBuiltQuery(LogQlDispatchKind.Rows, search.Sql, search.Parameters, HasGroupKey: false);
    }
}
