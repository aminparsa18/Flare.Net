using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>A parameterized exception-occurrences query plus its bound parameters.</summary>
public sealed record ExceptionOccurrenceSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="ExceptionOccurrencesRequest"/> → parameterized SQL builder for
/// <c>POST /api/errors/occurrences</c> - the exception-groups list's click-through
/// drill-down: every sample occurrence of one exact <c>(exception.type, exception.message)</c>
/// pair, most recent first. Same <c>ARRAY JOIN</c> shape as
/// <see cref="ExceptionGroupQueryBuilder"/> (see its remarks), narrowed by two more equality
/// predicates instead of a <c>GROUP BY</c>.
/// </summary>
/// <remarks>
/// Bounded to <see cref="MaxOccurrences"/>, no cursor/pagination - a click-through detail
/// list, not a primary explorer, same scope <see cref="ServiceCallBreakdownQueryBuilder"/>'s
/// external/database call lists keep for the analogous per-node drill-down.
/// </remarks>
public static class ExceptionOccurrenceQueryBuilder
{
    public const int MaxOccurrences = 50;

    public static ExceptionOccurrenceSql Build(ExceptionOccurrencesRequest request, DateTimeOffset now)
    {
        var filterSql = ExceptionFilterSqlBuilder.Build(request.Filter ?? new ExceptionFilter(), now);

        filterSql.Parameters.AddParameter("exceptionType", request.ExceptionType);
        filterSql.Parameters.AddParameter("exceptionMessage", request.ExceptionMessage);
        filterSql.Parameters.AddParameter("limit", (uint)MaxOccurrences);

        var sql = "SELECT\n" +
            "    TraceId,\n" +
            "    SpanId,\n" +
            "    ServiceName,\n" +
            "    Name AS SpanName,\n" +
            "    EventTime AS Timestamp,\n" +
            "    EventAttributes['exception.stacktrace'] AS Stacktrace\n" +
            "FROM spans\n" +
            "ARRAY JOIN Events.TimeUnixNano AS EventTime, Events.Name AS EventName, Events.Attributes AS EventAttributes\n" +
            $"WHERE {filterSql.WhereSql}\n" +
            "    AND EventAttributes['exception.type'] = {exceptionType:String}\n" +
            "    AND EventAttributes['exception.message'] = {exceptionMessage:String}\n" +
            "ORDER BY EventTime DESC\n" +
            "LIMIT {limit:UInt32}";

        return new ExceptionOccurrenceSql(sql, filterSql.Parameters);
    }
}
