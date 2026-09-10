using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>A parameterized exception-groups aggregate query plus its bound parameters.</summary>
public sealed record ExceptionGroupSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="ExceptionGroupsRequest"/> → parameterized SQL builder for
/// <c>POST /api/errors/groups</c> - one <c>GROUP BY (ExceptionType, ExceptionMessage)</c> over
/// every <c>exception</c>-named span event in the window. Same "pure function, unit-testable
/// on its own, split out of the query service" style as <see cref="ServiceOverviewQueryBuilder"/>.
/// </summary>
/// <remarks>
/// The first <c>ARRAY JOIN</c> over a Nested column anywhere in this codebase -
/// <see cref="SpanQueryService"/> zips <c>Events.Name</c>/<c>Events.Attributes</c> back
/// together in C#, not SQL, because it needs every event verbatim; this query instead needs
/// ClickHouse itself to walk and aggregate across them, which only <c>ARRAY JOIN</c> does.
/// ClickHouse walks multiple arrays from the same Nested column in lockstep by definition, so
/// <c>ARRAY JOIN Events.TimeUnixNano AS EventTime, Events.Name AS EventName,
/// Events.Attributes AS EventAttributes</c> re-pairs each event's own timestamp/name/
/// attributes correctly.
/// <para>
/// Exact <c>(exception.type, exception.message)</c> grouping, no fingerprint/template
/// normalization - see <see cref="ExceptionGroup"/>'s remarks for why that's a deliberate,
/// known limitation rather than an oversight.
/// </para>
/// <para>
/// No index helps this query - same unfiltered-by-service-or-any-equality-column tradeoff
/// <see cref="ServiceOverviewQueryBuilder"/>'s remarks document, except here there's an
/// additional <c>ARRAY JOIN</c> unrolling every span's events before the <c>WHERE</c> even
/// runs, so this is strictly heavier than that one - acceptable for a window-bounded,
/// investigate-on-demand page (no polling, see <c>ErrorsExplorerState</c>'s own remarks), not
/// something this query is optimized to run continuously.
/// </para>
/// </remarks>
public static class ExceptionGroupQueryBuilder
{
    /// <summary>Same defaults as <see cref="LogPatternQueryBuilder"/>'s own TopN clamp - this is the same shape of "ranked, bounded aggregate list" query.</summary>
    public const int DefaultTopN = 200;
    private const int MaxTopN = 1_000;

    public static ExceptionGroupSql Build(ExceptionGroupsRequest request, DateTimeOffset now)
    {
        // Same System.Text.Json init-only-property caveat SpanSearchQueryBuilder guards
        // against - request.Filter's `= new()` default doesn't survive deserialization when
        // the JSON body omits "filter".
        var filterSql = ExceptionFilterSqlBuilder.Build(request.Filter ?? new ExceptionFilter(), now);

        var topN = Math.Clamp(request.TopN is > 0 ? request.TopN.Value : DefaultTopN, 1, MaxTopN);
        filterSql.Parameters.AddParameter("topN", (uint)topN);

        var sql = "SELECT\n" +
            "    EventAttributes['exception.type'] AS ExceptionType,\n" +
            "    EventAttributes['exception.message'] AS ExceptionMessage,\n" +
            "    count() AS OccurrenceCount,\n" +
            "    min(EventTime) AS FirstSeen,\n" +
            "    max(EventTime) AS LastSeen,\n" +
            "    groupUniqArray(ServiceName) AS AffectedServices\n" +
            "FROM spans\n" +
            "ARRAY JOIN Events.TimeUnixNano AS EventTime, Events.Name AS EventName, Events.Attributes AS EventAttributes\n" +
            $"WHERE {filterSql.WhereSql} AND EventAttributes['exception.type'] != ''\n" +
            "GROUP BY ExceptionType, ExceptionMessage\n" +
            "ORDER BY OccurrenceCount DESC\n" +
            "LIMIT {topN:UInt32}";

        return new ExceptionGroupSql(sql, filterSql.Parameters);
    }
}
