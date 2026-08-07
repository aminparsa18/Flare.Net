using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT</c> for <c>/api/logs/search</c>, ready to hand to <see cref="LogQueryService"/>.</summary>
public sealed record LogSearchSql(string Sql, ClickHouse.Driver.ADO.Parameters.ClickHouseParameterCollection Parameters, int PageSize);

/// <summary>
/// Pure <see cref="LogSearchRequest"/> → parameterized SQL builder. No ClickHouse
/// dependency - unit-testable on its own, same style as <see cref="LogFilterSqlBuilder"/>.
/// </summary>
public static class LogSearchQueryBuilder
{
    public const int DefaultPageSize = 200;
    public const int MaxPageSize = 1000;

    public static LogSearchSql Build(LogSearchRequest request, DateTimeOffset now)
    {
        // request.Filter's `= new()` property initializer is a compile-time default
        // for C# callers - it does NOT survive System.Text.Json deserialization when
        // the JSON body omits "filter" entirely (confirmed live: STJ always assigns
        // init-only properties via object-initializer during deserialization, using
        // `default(T)` for anything absent from the payload, which overwrites the
        // initializer-set default back to null). Coalesce defensively rather than
        // trust the property default once JSON is in the picture.
        var filterSql = LogFilterSqlBuilder.Build(request.Filter ?? new LogFilter(), now);
        var clauses = new List<string> { filterSql.WhereSql };

        if (LogSearchCursor.TryDecode(request.Cursor) is { } cursor)
        {
            filterSql.Parameters.AddParameter("cursorTs", cursor.Timestamp.UtcDateTime);
            filterSql.Parameters.AddParameter("cursorId", cursor.EventId);
            clauses.Add("(Timestamp, EventId) < ({cursorTs:DateTime64(9)}, {cursorId:UUID})");
        }

        var pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, 1, MaxPageSize);
        // Fetch one extra row so LogQueryService can tell "more pages exist" apart from
        // "this page happened to end exactly at pageSize" without a separate count query.
        filterSql.Parameters.AddParameter("limit", (uint)(pageSize + 1));

        // Built via concatenation rather than one interpolated string: the literal
        // "{limit:UInt64}" is a ClickHouse parameter placeholder, not C# interpolation
        // syntax - mixing the two inside a single `$"..."` is ambiguous (C# tries to
        // parse "limit:UInt64" as an interpolation hole with a format specifier).
        var sql = $"SELECT {LogEventColumns.SelectList}\n" +
            "FROM logs\n" +
            $"WHERE {string.Join(" AND ", clauses)}\n" +
            "ORDER BY Timestamp DESC, EventId DESC\n" +
            "LIMIT {limit:UInt64}";

        return new LogSearchSql(sql, filterSql.Parameters, pageSize);
    }
}
