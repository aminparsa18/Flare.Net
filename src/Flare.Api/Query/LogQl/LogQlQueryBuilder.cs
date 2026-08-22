using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using System.Linq;

namespace Flare.Api.Query.LogQl;

/// <summary>Which shape <see cref="LogQlQueryBuilder.Build"/> dispatched to - drives how <c>LogQueryService.RunQlQueryAsync</c> reads the result set back.</summary>
public enum LogQlDispatchKind
{
    Count,
    Series,
    Rows,
    Table,
}

/// <summary>
/// Fully-built SQL for one SQL-query-row request, ready for <see cref="LogQueryService"/>.
/// <paramref name="Columns"/> is populated only for <see cref="LogQlDispatchKind.Table"/> -
/// the selected columns' display names, in select order, for the response's <c>Columns</c>
/// (and to know how many ordinals to read per row).
/// </summary>
public sealed record LogQlBuiltQuery(LogQlDispatchKind Kind, string Sql, ClickHouseParameterCollection Parameters, bool HasGroupKey, IReadOnlyList<string>? Columns = null);

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

        if (query.Select is LogQlSelectAggregate aggregateSelect)
        {
            // "toFloat64(...)" wraps every aggregate here (count() included) so this
            // path's own reader (LogQueryService.RunQlQueryAsync) can always read a plain
            // double regardless of which function ran, rather than branching per function -
            // avg()/sum() are genuinely fractional (ClickHouse returns Float64 for them
            // already), and wrapping count() (normally UInt64) the same way costs nothing
            // since it's always integral anyway. Deliberately NOT how /api/logs/aggregate's
            // own count()-only path reads its result (see LogAggregateQueryBuilder's
            // BuildFromFilterSql remarks) - that endpoint is untouched.
            var aggregateSql = $"toFloat64({AggregateSql(aggregateSelect)})";

            if (query.GroupBy is { } groupBy)
            {
                var aggregate = LogAggregateQueryBuilder.BuildFromFilterSql(filterSql, groupBy.TimeBucketSeconds, groupBy.Secondary, aggregateSql);
                return new LogQlBuiltQuery(LogQlDispatchKind.Series, aggregate.Sql, aggregate.Parameters, aggregate.HasGroupKey);
            }

            var sql = $"SELECT {aggregateSql} FROM logs WHERE {filterSql.WhereSql}";
            return new LogQlBuiltQuery(LogQlDispatchKind.Count, sql, filterSql.Parameters, HasGroupKey: false);
        }

        // Parser already rejected GroupBy paired with a non-aggregate select (Star/Columns) -
        // see LogQlParser.Parse's trailing validation - so only Star/Columns reach here.
        if (query.Select is LogQlSelectColumns columnsSelect)
        {
            var columnNames = columnsSelect.Columns.Select(LogQlWhereTranslator.ColumnName).ToArray();
            var displayNames = columnsSelect.Columns.Select(c => c.ToString()).ToArray();

            filterSql.Parameters.AddParameter("limit", (uint)(RawRowLimit + 1));
            var tableSql = $"SELECT {string.Join(", ", columnNames)}\n" +
                "FROM logs\n" +
                $"WHERE {filterSql.WhereSql}\n" +
                "ORDER BY Timestamp DESC\n" +
                "LIMIT {limit:UInt64}";
            return new LogQlBuiltQuery(LogQlDispatchKind.Table, tableSql, filterSql.Parameters, HasGroupKey: false, Columns: displayNames);
        }

        var search = LogSearchQueryBuilder.BuildFromFilterSql(filterSql, cursor: null, pageSize: RawRowLimit);
        return new LogQlBuiltQuery(LogQlDispatchKind.Rows, search.Sql, search.Parameters, HasGroupKey: false);
    }

    /// <summary>
    /// Fixed, closed-set SQL fragment for one aggregate select - never request text, built
    /// entirely from the parsed <see cref="LogQlAggFunc"/>/<see cref="LogQlColumn"/> enums
    /// (same discipline as every other interpolated fragment in this namespace).
    /// </summary>
    private static string AggregateSql(LogQlSelectAggregate select) => select.Func switch
    {
        LogQlAggFunc.Count => "count()",
        LogQlAggFunc.Avg => $"avg({LogQlWhereTranslator.ColumnName(select.Column!.Value)})",
        LogQlAggFunc.Sum => $"sum({LogQlWhereTranslator.ColumnName(select.Column!.Value)})",
        _ => throw new InvalidOperationException($"Unhandled LogQlAggFunc '{select.Func}'."),
    };
}
