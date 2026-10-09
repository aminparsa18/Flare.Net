using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT</c> for the <c>/sessions</c> page, ready to hand to <see cref="AppSessionQueryService"/>.</summary>
public sealed record AppSessionSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure SQL builder for <c>POST /api/app-sessions/list</c>. A session is every span sharing a
/// <c>session.id</c> span attribute inside the window; nothing is stored separately (ADR-0167),
/// so there is no new table or migration. Device and version fields come from the resource
/// attributes <c>Flare.Maui</c> sets (ADR-0166) and are taken with <c>any()</c>: they are the
/// same for every span of one process start.
/// </summary>
/// <remarks>
/// The <c>mapContains</c> guard lets <c>idx_span_attr_key</c> (0007_spans.sql) skip granules
/// with no session attribute at all, so a window of server-only traces costs almost nothing.
/// A session straddling the window edge is reported with only the part inside it.
/// </remarks>
public static class AppSessionQueryBuilder
{
    public const int DefaultWindowMinutes = 60;
    public const int MinWindowMinutes = 5;
    public const int MaxWindowMinutes = 10080;

    /// <summary>Row cap for the table. One more is fetched so the caller can tell it was cut.</summary>
    public const int MaxRows = 500;

    public const int MaxScreensPerSession = 8;

    /// <summary>Span cap for one session's timeline. One more is fetched so the caller can tell it was cut.</summary>
    public const int MaxTimelineRows = 1000;

    public const int DefaultTimelineLookbackMinutes = 1440;

    /// <summary>Widest timeline window, matching <see cref="MaxWindowMinutes"/>.</summary>
    public const int MaxTimelineWindowMinutes = MaxWindowMinutes;

    private const string SessionId = "SpanAttributes['session.id']";

    public static int ClampWindowMinutes(int? requested) =>
        requested is > 0 ? Math.Clamp(requested.Value, MinWindowMinutes, MaxWindowMinutes) : DefaultWindowMinutes;

    public static DateTimeOffset ResolveWindowEnd(long? endUnixMs, DateTimeOffset now) =>
        HostInventoryQueryBuilder.ResolveWindowEnd(endUnixMs, now);

    /// <summary>
    /// One row per session, most recently active first. Columns: SessionId, ServiceName, Version,
    /// Os, Device, FirstSeenUnixMs, LastSeenUnixMs, SpanCount, TraceCount, ErrorCount, Screens.
    /// </summary>
    public static AppSessionSql BuildSessions(AppSessionsRequest request, int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = Where(parameters, windowMinutes, end, request.Service, request.Version);
        parameters.AddParameter("limit", (uint)(MaxRows + 1));

        var having = request.ErrorsOnly ? "\nHAVING ErrorCount > 0" : string.Empty;
        var sql = "SELECT\n" +
            $"    {SessionId} AS SessionId,\n" +
            "    any(ServiceName) AS Service,\n" +
            "    any(ResourceAttributes['service.version']) AS Version,\n" +
            "    trim(concat(any(ResourceAttributes['os.type']), ' ', any(ResourceAttributes['os.version']))) AS Os,\n" +
            "    any(ResourceAttributes['device.model.identifier']) AS Device,\n" +
            "    toUnixTimestamp64Milli(min(StartTime)) AS FirstSeenUnixMs,\n" +
            "    toUnixTimestamp64Milli(max(StartTime)) AS LastSeenUnixMs,\n" +
            "    count() AS SpanCount,\n" +
            "    uniqExact(TraceId) AS TraceCount,\n" +
            "    countIf(StatusCode = 'STATUS_CODE_ERROR') AS ErrorCount,\n" +
            $"    groupUniqArrayIf({MaxScreensPerSession})(SpanAttributes['screen.name'], SpanAttributes['screen.name'] != '') AS Screens\n" +
            "FROM spans\n" +
            $"WHERE {where}\n" +
            $"GROUP BY SessionId{having}\n" +
            "ORDER BY LastSeenUnixMs DESC, SessionId\n" +
            "LIMIT {limit:UInt32}";

        return new AppSessionSql(sql, parameters);
    }

    /// <summary>
    /// Every service and every version that had a session in the window, as two sorted arrays in
    /// one row. Ignores the request's own service/version filters so picking one doesn't hide the others.
    /// </summary>
    public static AppSessionSql BuildFacets(int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = Where(parameters, windowMinutes, end, service: null, version: null);
        var sql = "SELECT\n" +
            "    arraySort(groupUniqArray(ServiceName)) AS Services,\n" +
            "    arraySort(groupUniqArrayIf(ResourceAttributes['service.version'], ResourceAttributes['service.version'] != '')) AS Versions\n" +
            "FROM spans\n" +
            $"WHERE {where}";
        return new AppSessionSql(sql, parameters);
    }

    /// <summary>
    /// Resolves a timeline request's bounds: <c>to</c> defaults to now, <c>from</c> to a day before it,
    /// and the span is capped at <see cref="MaxTimelineWindowMinutes"/> (the end wins, so an over-wide
    /// request keeps its most recent part). Never inverted.
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To) ResolveTimelineWindow(AppSessionTimelineRequest request, DateTimeOffset now)
    {
        var to = request.ToUnixMs is > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(request.ToUnixMs.Value) : now;
        var from = request.FromUnixMs is > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(request.FromUnixMs.Value)
            : to.AddMinutes(-DefaultTimelineLookbackMinutes);
        if (from >= to) from = to.AddMinutes(-MinWindowMinutes);
        var earliest = to.AddMinutes(-MaxTimelineWindowMinutes);
        return (from < earliest ? earliest : from, to);
    }

    /// <summary>
    /// One session's spans, oldest first. Columns: TraceId, SpanId, Name, ServiceName, StartUnixMs,
    /// DurationMs, Screen, IsError, StatusMessage, ExceptionType, ExceptionMessage, Version, Os, Device.
    /// </summary>
    public static AppSessionSql BuildTimeline(string sessionId, DateTimeOffset from, DateTimeOffset to)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("from", from.UtcDateTime);
        parameters.AddParameter("to", to.UtcDateTime);
        parameters.AddParameter("sessionId", sessionId);
        parameters.AddParameter("limit", (uint)(MaxTimelineRows + 1));

        var clauses = new List<string>
        {
            "StartTime >= {from:DateTime64(9)}",
            "StartTime < {to:DateTime64(9)}",
            "mapContains(SpanAttributes, 'session.id')",
            $"{SessionId} = {{sessionId:String}}",
        };
        ServiceScope.Append(clauses, parameters);

        var sql = "SELECT\n" +
            "    TraceId, SpanId, Name, ServiceName,\n" +
            "    toUnixTimestamp64Milli(StartTime) AS StartUnixMs,\n" +
            "    DurationNano / 1000000. AS DurationMs,\n" +
            "    SpanAttributes['screen.name'] AS Screen,\n" +
            "    StatusCode = 'STATUS_CODE_ERROR' AS IsError,\n" +
            "    StatusMessage,\n" +
            "    arrayFirst(x -> x != '', arrayMap(a -> a['exception.type'], Events.Attributes)) AS ExceptionType,\n" +
            "    arrayFirst(x -> x != '', arrayMap(a -> a['exception.message'], Events.Attributes)) AS ExceptionMessage,\n" +
            "    ResourceAttributes['service.version'] AS Version,\n" +
            "    trim(concat(ResourceAttributes['os.type'], ' ', ResourceAttributes['os.version'])) AS Os,\n" +
            "    ResourceAttributes['device.model.identifier'] AS Device\n" +
            "FROM spans\n" +
            $"WHERE {string.Join(" AND ", clauses)}\n" +
            "ORDER BY StartTime, SpanId\n" +
            "LIMIT {limit:UInt32}";
        return new AppSessionSql(sql, parameters);
    }

    private static string Where(ClickHouseParameterCollection parameters, int windowMinutes, DateTimeOffset end, string? service, string? version)
    {
        parameters.AddParameter("from", end.AddMinutes(-windowMinutes).UtcDateTime);
        parameters.AddParameter("to", end.UtcDateTime);

        var clauses = new List<string>
        {
            "StartTime >= {from:DateTime64(9)}",
            "StartTime < {to:DateTime64(9)}",
            "mapContains(SpanAttributes, 'session.id')",
            $"{SessionId} != ''",
        };

        if (!string.IsNullOrWhiteSpace(service))
        {
            parameters.AddParameter("service", service);
            clauses.Add("ServiceName = {service:String}");
        }

        if (!string.IsNullOrWhiteSpace(version))
        {
            parameters.AddParameter("version", version);
            clauses.Add("ResourceAttributes['service.version'] = {version:String}");
        }

        ServiceScope.Append(clauses, parameters);
        return string.Join(" AND ", clauses);
    }
}
