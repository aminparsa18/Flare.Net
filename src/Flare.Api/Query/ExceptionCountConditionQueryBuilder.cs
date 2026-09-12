using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>A fully-built single-row count <c>SELECT</c> for one <see cref="ExceptionCountCondition"/> over one evaluation window.</summary>
public sealed record ExceptionCountConditionSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="ExceptionCountCondition"/> + window -> parameterized SQL builder, for
/// <c>IAlertQueryService.CountMatchingExceptionsAsync</c>. Reuses
/// <see cref="ExceptionFilterSqlBuilder"/> - the same <c>WHERE</c> fragment
/// <see cref="ExceptionGroupQueryBuilder"/>/<c>ExceptionOccurrenceQueryBuilder</c> already
/// build on - and adds an exact <c>exception.type</c> match (plus an optional
/// <c>exception.message</c> match), then <c>count()</c>s instead of grouping. Deliberately its
/// own builder rather than reusing <see cref="ExceptionGroupQueryBuilder"/>: that one groups
/// every distinct (type, message) pair for a list view; an alert condition wants one scalar
/// count for exactly one already-known type (optionally narrowed to one message), so there's
/// no grouping/top-N/ordering to reuse.
/// </summary>
public static class ExceptionCountConditionQueryBuilder
{
    public static ExceptionCountConditionSql Build(ExceptionCountCondition condition, DateTimeOffset from, DateTimeOffset to)
    {
        // Same System.Text.Json init-only-property caveat MetricAlertConditionQueryBuilder
        // guards against - condition.Filter's `= new()` default doesn't survive
        // deserialization when the persisted/request JSON omits "filter".
        var windowedFilter = (condition.Filter ?? new ExceptionFilter()) with { From = from, To = to };
        var filterSql = ExceptionFilterSqlBuilder.Build(windowedFilter, to);

        filterSql.Parameters.AddParameter("exceptionType", condition.ExceptionType);
        var clauses = $"{filterSql.WhereSql} AND EventAttributes['exception.type'] = {{exceptionType:String}}";

        // Empty ExceptionMessage (the default) deliberately matches every message for the
        // type - see that property's own doc comment.
        if (!string.IsNullOrEmpty(condition.ExceptionMessage))
        {
            filterSql.Parameters.AddParameter("exceptionMessage", condition.ExceptionMessage);
            clauses += " AND EventAttributes['exception.message'] = {exceptionMessage:String}";
        }

        var sql = "SELECT count() FROM spans\n" +
            "ARRAY JOIN Events.TimeUnixNano AS EventTime, Events.Name AS EventName, Events.Attributes AS EventAttributes\n" +
            $"WHERE {clauses}";

        return new ExceptionCountConditionSql(sql, filterSql.Parameters);
    }
}
