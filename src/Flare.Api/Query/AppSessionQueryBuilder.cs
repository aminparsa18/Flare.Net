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
    /// Release health: one row per <c>service.version</c> with its session and user counts, crashed and errored
    /// ones included (ADR-0175). A session is crashed when it has an <c>app.unhandled_exception</c> span with
    /// <c>exception.escaped = true</c> (what <c>Flare.Maui</c> reports for a fatal exception) and errored when any
    /// span has an error status. A user is <c>user.id</c> on a span or resource, empty when the app sets none.
    /// Columns: Version, Sessions, CrashedSessions, ErroredSessions, Users, CrashedUsers; most sessions first.
    /// </summary>
    public static AppSessionSql BuildReleaseHealth(ReleaseHealthRequest request, int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = Where(parameters, windowMinutes, end, request.Service, version: null);
        var sql = "SELECT\n" +
            "    Version,\n" +
            "    count() AS Sessions,\n" +
            "    countIf(Crashed) AS CrashedSessions,\n" +
            "    countIf(Errored) AS ErroredSessions,\n" +
            "    uniqExactIf(UserId, UserId != '') AS Users,\n" +
            "    uniqExactIf(UserId, UserId != '' AND Crashed) AS CrashedUsers\n" +
            "FROM (\n" +
            "    SELECT\n" +
            "        any(ResourceAttributes['service.version']) AS Version,\n" +
            "        anyIf(if(SpanAttributes['user.id'] != '', SpanAttributes['user.id'], ResourceAttributes['user.id']), " +
            "SpanAttributes['user.id'] != '' OR ResourceAttributes['user.id'] != '') AS UserId,\n" +
            "        countIf(Name = 'app.unhandled_exception' AND lower(SpanAttributes['exception.escaped']) = 'true') > 0 AS Crashed,\n" +
            "        countIf(StatusCode = 'STATUS_CODE_ERROR') > 0 AS Errored\n" +
            "    FROM spans\n" +
            $"    WHERE {where}\n" +
            $"    GROUP BY {SessionId}\n" +
            ")\n" +
            "GROUP BY Version\n" +
            "ORDER BY Sessions DESC, Version";
        return new AppSessionSql(sql, parameters);
    }

    /// <summary>Most buckets a trend may span, so a tiny bucket over a week can't return an enormous series.</summary>
    public const int MaxTrendBuckets = 500;

    private static readonly int[] NiceBucketMinutes = [5, 10, 15, 30, 60, 120, 180, 360, 720, 1440];

    /// <summary>
    /// A requested bucket width when it is positive and keeps the window within <see cref="MaxTrendBuckets"/>, otherwise
    /// the smallest round width (5 minutes to a day) giving at most 60 buckets.
    /// </summary>
    public static int ResolveTrendBucketMinutes(int? requested, int windowMinutes)
    {
        if (requested is > 0 && (windowMinutes + requested.Value - 1) / requested.Value <= MaxTrendBuckets)
            return requested.Value;

        foreach (var minutes in NiceBucketMinutes)
        {
            if ((windowMinutes + minutes - 1) / minutes <= 60) return minutes;
        }

        return NiceBucketMinutes[^1];
    }

    /// <summary>
    /// Crash-free trend: each session is counted once, in the bucket of its first span, under its version (ADR-0189).
    /// Same session definition as <see cref="BuildReleaseHealth"/>. Columns: BucketUnixMs, Version, Sessions,
    /// CrashedSessions; oldest bucket first.
    /// </summary>
    public static AppSessionSql BuildReleaseHealthTrend(ReleaseHealthTrendRequest request, int windowMinutes, int bucketMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = Where(parameters, windowMinutes, end, request.Service, version: null);
        var sql = "SELECT\n" +
            $"    toUnixTimestamp64Milli(toDateTime64(toStartOfInterval(FirstSeen, INTERVAL {bucketMinutes} MINUTE), 3)) AS BucketUnixMs,\n" +
            "    Version,\n" +
            "    count() AS Sessions,\n" +
            "    countIf(Crashed) AS CrashedSessions\n" +
            "FROM (\n" +
            "    SELECT\n" +
            "        min(StartTime) AS FirstSeen,\n" +
            "        any(ResourceAttributes['service.version']) AS Version,\n" +
            "        countIf(Name = 'app.unhandled_exception' AND lower(SpanAttributes['exception.escaped']) = 'true') > 0 AS Crashed\n" +
            "    FROM spans\n" +
            $"    WHERE {where}\n" +
            $"    GROUP BY {SessionId}\n" +
            ")\n" +
            "GROUP BY BucketUnixMs, Version\n" +
            "ORDER BY BucketUnixMs, Version";
        return new AppSessionSql(sql, parameters);
    }

    /// <summary>Row cap for <see cref="BuildReleaseHealthSeries"/>: buckets x services x versions. One more is fetched so the caller can tell it was cut.</summary>
    public const int MaxSeriesRows = 50_000;

    /// <summary>
    /// Release health as a time series for the Prometheus API (ADR-0250): per epoch-aligned bucket, service and version, the
    /// sessions whose first span fell in the bucket, how many of them crashed, and the distinct users in them and in the
    /// crashed ones. Same session definition as <see cref="BuildReleaseHealth"/>. Columns: BucketUnixMs, ServiceName, Version,
    /// Sessions, CrashedSessions, Users, CrashedUsers; oldest bucket first.
    /// </summary>
    public static AppSessionSql BuildReleaseHealthSeries(DateTimeOffset from, DateTimeOffset to, int bucketSeconds, string? service)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = Where(parameters, from, to, service, version: null);
        var bucketMs = checked(bucketSeconds * 1000L);
        var sql = "SELECT\n" +
            $"    toInt64(intDiv(toUnixTimestamp64Milli(FirstSeen), {bucketMs}) * {bucketMs}) AS BucketUnixMs,\n" +
            "    Svc AS ServiceName,\n" +
            "    Version,\n" +
            "    count() AS Sessions,\n" +
            "    countIf(Crashed) AS CrashedSessions,\n" +
            "    uniqExactIf(UserId, UserId != '') AS Users,\n" +
            "    uniqExactIf(UserId, UserId != '' AND Crashed) AS CrashedUsers\n" +
            "FROM (\n" +
            "    SELECT\n" +
            "        min(StartTime) AS FirstSeen,\n" +
            "        any(ServiceName) AS Svc,\n" +
            "        any(ResourceAttributes['service.version']) AS Version,\n" +
            "        anyIf(if(SpanAttributes['user.id'] != '', SpanAttributes['user.id'], ResourceAttributes['user.id']), " +
            "SpanAttributes['user.id'] != '' OR ResourceAttributes['user.id'] != '') AS UserId,\n" +
            "        countIf(Name = 'app.unhandled_exception' AND lower(SpanAttributes['exception.escaped']) = 'true') > 0 AS Crashed\n" +
            "    FROM spans\n" +
            $"    WHERE {where}\n" +
            $"    GROUP BY {SessionId}\n" +
            ")\n" +
            "GROUP BY BucketUnixMs, Svc, Version\n" +
            "ORDER BY BucketUnixMs, Svc, Version\n" +
            $"LIMIT {MaxSeriesRows + 1}";
        return new AppSessionSql(sql, parameters);
    }

    /// <summary>
    /// Distinct users over a trailing window, per epoch-aligned step, for the Prometheus API's <c>flare_release_users_24h</c>
    /// gauges (ADR-0251). A step's value is the users with a session that started in the <paramref name="windowSeconds"/> up to
    /// and including that step (rounded down to whole steps, at least one). Columns: BucketUnixMs, ServiceName, Version,
    /// Users, CrashedUsers; oldest step first. Scans <paramref name="windowSeconds"/> before <paramref name="from"/> too.
    /// </summary>
    /// <remarks>
    /// Distinct users do not add up across steps, and a set per step would cost users x window / step. Instead each user's
    /// session steps are merged into the disjoint runs of steps they are covered in, and a run is expanded into the steps it
    /// covers (clamped to the requested range) and counted, so the work is the number of (user, step) pairs and memory stays
    /// at one counter per step, service and version.
    /// </remarks>
    public static AppSessionSql BuildRollingReleaseUsers(DateTimeOffset from, DateTimeOffset to, int bucketSeconds, int windowSeconds, string? service)
    {
        var parameters = new ClickHouseParameterCollection();
        var bucketMs = checked(bucketSeconds * 1000L);
        var windowMs = Math.Max(windowSeconds * 1000L / bucketMs, 1) * bucketMs;
        var first = Math.DivRem(from.ToUnixTimeMilliseconds(), bucketMs, out _) * bucketMs;
        var last = Math.DivRem(to.ToUnixTimeMilliseconds() - 1, bucketMs, out _) * bucketMs;
        var where = Where(parameters, from.AddMilliseconds(-windowMs), to, service, version: null);

        // Runs of covered steps over a sorted array of session steps X: a step b covers b .. b + window - bucket, and a run
        // starts where the gap to the previous step is more than a window (otherwise the coverage is contiguous).
        static string Runs(string x, long windowMs, long bucketMs, string crashed) =>
            $"arrayMap((s, e) -> (s, e, {crashed}), " +
            $"arrayFilter((b, i) -> i = 1 OR b - {x}[i - 1] > {windowMs}, {x}, arrayEnumerate({x})), " +
            $"arrayMap(b -> b + {windowMs - bucketMs}, arrayFilter((b, i) -> i = length({x}) OR {x}[i + 1] - b > {windowMs}, {x}, arrayEnumerate({x}))))";

        var sql = "SELECT\n" +
            "    toInt64(Step) AS BucketUnixMs,\n" +
            "    Svc AS ServiceName,\n" +
            "    Version,\n" +
            "    countIf(Run.3 = 0) AS Users,\n" +
            "    countIf(Run.3 = 1) AS CrashedUsers\n" +
            "FROM (\n" +
            "    SELECT\n" +
            "        Svc,\n" +
            "        Version,\n" +
            $"        arrayJoin(arrayConcat({Runs("Steps", windowMs, bucketMs, "0")}, {Runs("CrashedSteps", windowMs, bucketMs, "1")})) AS Run\n" +
            "    FROM (\n" +
            "        SELECT\n" +
            "            Svc,\n" +
            "            Version,\n" +
            "            arraySort(groupUniqArray(SessionStep)) AS Steps,\n" +
            "            arraySort(groupUniqArrayIf(SessionStep, Crashed)) AS CrashedSteps\n" +
            "        FROM (\n" +
            "            SELECT\n" +
            $"                toUInt64(intDiv(toUnixTimestamp64Milli(min(StartTime)), {bucketMs}) * {bucketMs}) AS SessionStep,\n" +
            "                any(ServiceName) AS Svc,\n" +
            "                any(ResourceAttributes['service.version']) AS Version,\n" +
            "                anyIf(if(SpanAttributes['user.id'] != '', SpanAttributes['user.id'], ResourceAttributes['user.id']), " +
            "SpanAttributes['user.id'] != '' OR ResourceAttributes['user.id'] != '') AS UserId,\n" +
            "                countIf(Name = 'app.unhandled_exception' AND lower(SpanAttributes['exception.escaped']) = 'true') > 0 AS Crashed\n" +
            "            FROM spans\n" +
            $"            WHERE {where}\n" +
            $"            GROUP BY {SessionId}\n" +
            "            HAVING UserId != ''\n" +
            "        )\n" +
            "        GROUP BY Svc, Version, UserId\n" +
            "    )\n" +
            ")\n" +
            $"ARRAY JOIN range(greatest(toUInt64(Run.1), {first}), least(toUInt64(Run.2), {last}) + 1, {bucketMs}) AS Step\n" +
            "GROUP BY Step, Svc, Version\n" +
            "ORDER BY Step, Svc, Version\n" +
            $"LIMIT {MaxSeriesRows + 1}";
        return new AppSessionSql(sql, parameters);
    }

    /// <summary>Row cap for each performance table. Neither is expected to reach it: one row per screen, or per version and start type.</summary>
    public const int MaxPerformanceRows = 200;

    /// <summary>
    /// App start durations per version and start type (<c>app.start</c> spans, ADR-0180). Columns: Version, Type,
    /// Count, P50Ms, P95Ms; newest-looking version (descending) first, cold before warm.
    /// </summary>
    public static AppSessionSql BuildAppStarts(AppPerformanceRequest request, int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = Where(parameters, windowMinutes, end, request.Service, request.Version);
        var sql = "SELECT\n" +
            "    ResourceAttributes['service.version'] AS Version,\n" +
            "    SpanAttributes['app.start.type'] AS Type,\n" +
            "    count() AS Count,\n" +
            "    quantile(0.5)(DurationNano / 1000000.) AS P50Ms,\n" +
            "    quantile(0.95)(DurationNano / 1000000.) AS P95Ms\n" +
            "FROM spans\n" +
            $"WHERE {where} AND Name = 'app.start'\n" +
            "GROUP BY Version, Type\n" +
            "ORDER BY Version DESC, Type\n" +
            $"LIMIT {MaxPerformanceRows}";
        return new AppSessionSql(sql, parameters);
    }

    /// <summary>
    /// Load time and frame health per screen (<c>screen.load</c> and <c>screen.frames</c> spans, ADR-0180). Columns:
    /// Screen, Loads, LoadP50Ms, LoadP95Ms, Visits, Frames, SlowFrames, FrozenFrames; slowest loads first.
    /// A load percentile with no loads is 0, not NaN, so the response stays valid JSON.
    /// </summary>
    public static AppSessionSql BuildScreenPerformance(AppPerformanceRequest request, int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = Where(parameters, windowMinutes, end, request.Service, request.Version);
        const string screen = "SpanAttributes['screen.name']";
        var sql = "SELECT\n" +
            $"    {screen} AS Screen,\n" +
            "    countIf(Name = 'screen.load') AS Loads,\n" +
            "    if(Loads = 0, 0, quantileIf(0.5)(DurationNano / 1000000., Name = 'screen.load')) AS LoadP50Ms,\n" +
            "    if(Loads = 0, 0, quantileIf(0.95)(DurationNano / 1000000., Name = 'screen.load')) AS LoadP95Ms,\n" +
            "    countIf(Name = 'screen.frames') AS Visits,\n" +
            "    sumIf(toUInt64OrZero(SpanAttributes['frames.total']), Name = 'screen.frames') AS Frames,\n" +
            "    sumIf(toUInt64OrZero(SpanAttributes['frames.slow']), Name = 'screen.frames') AS SlowFrames,\n" +
            "    sumIf(toUInt64OrZero(SpanAttributes['frames.frozen']), Name = 'screen.frames') AS FrozenFrames\n" +
            "FROM spans\n" +
            $"WHERE {where} AND Name IN ('screen.load', 'screen.frames') AND {screen} != ''\n" +
            "GROUP BY Screen\n" +
            "ORDER BY LoadP95Ms DESC, Screen\n" +
            $"LIMIT {MaxPerformanceRows}";
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
    /// DurationMs, Screen, IsError, StatusMessage, ExceptionType, ExceptionMessage, Version, Os, Device,
    /// BreadcrumbCategory, BreadcrumbMessage.
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
            "    ResourceAttributes['device.model.identifier'] AS Device,\n" +
            "    SpanAttributes['breadcrumb.category'] AS BreadcrumbCategory,\n" +
            "    SpanAttributes['breadcrumb.message'] AS BreadcrumbMessage\n" +
            "FROM spans\n" +
            $"WHERE {string.Join(" AND ", clauses)}\n" +
            "ORDER BY StartTime, SpanId\n" +
            "LIMIT {limit:UInt32}";
        return new AppSessionSql(sql, parameters);
    }

    /// <summary>Most scheduled frames one replay response carries (an hour at the 5-second minimum interval).</summary>
    public const int MaxReplayFrames = 720;

    /// <summary>
    /// Span ids in the window that have an error screenshot for the session (for the timeline's flag). One column:
    /// SpanId. Scheduled frames have an empty span id (ADR-0192) and are left out.
    /// </summary>
    public static AppSessionSql BuildScreenshotSpans(string sessionId, DateTimeOffset from, DateTimeOffset to) =>
        BuildScreenshotQuery("SELECT DISTINCT SpanId", sessionId, null, from, to, "", "SpanId != ''");

    /// <summary>The session's scheduled screenshot times, oldest first, one extra row to detect truncation. One column: StartUnixMs.</summary>
    public static AppSessionSql BuildReplayFrames(string sessionId, DateTimeOffset from, DateTimeOffset to) =>
        BuildScreenshotQuery("SELECT toUnixTimestamp64Milli(StartTime) AS StartUnixMs", sessionId, null, from, to,
            $"ORDER BY StartTime\nLIMIT {MaxReplayFrames + 1}", "Kind = 'periodic'");

    /// <summary>The scheduled screenshot taken at <paramref name="timeUnixMs"/>. Columns: ContentType, ImageBase64.</summary>
    public static AppSessionSql BuildReplayFrame(string sessionId, long timeUnixMs)
    {
        var at = DateTimeOffset.FromUnixTimeMilliseconds(timeUnixMs);
        return BuildScreenshotQuery("SELECT ContentType, ImageBase64", sessionId, null, at, at.AddMilliseconds(1),
            "ORDER BY StartTime DESC\nLIMIT 1", "Kind = 'periodic'");
    }

    /// <summary>Row cap for the feedback inbox. One more is fetched so the caller can tell it was cut.</summary>
    public const int MaxFeedbackRows = 200;

    /// <summary>
    /// User feedback in the window, newest first. Columns: FeedbackId, TimeUnixMs, ServiceName, SessionId, UserId, Name,
    /// Email, AppVersion, Message (ADR-0193).
    /// </summary>
    public static AppSessionSql BuildFeedback(AppFeedbackRequest request, int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("from", end.AddMinutes(-windowMinutes).UtcDateTime);
        parameters.AddParameter("to", end.UtcDateTime);
        var clauses = new List<string> { "StartTime >= {from:DateTime64(3)}", "StartTime < {to:DateTime64(3)}" };
        if (!string.IsNullOrEmpty(request.Service))
        {
            parameters.AddParameter("service", request.Service);
            clauses.Add("ServiceName = {service:String}");
        }
        if (!string.IsNullOrEmpty(request.SessionId))
        {
            parameters.AddParameter("sessionId", request.SessionId);
            clauses.Add("SessionId = {sessionId:String}");
        }
        ServiceScope.Append(clauses, parameters);
        return new AppSessionSql(
            "SELECT toString(FeedbackId), toUnixTimestamp64Milli(StartTime), ServiceName, SessionId, UserId, Name, Email, AppVersion, Message\n" +
            $"FROM app_feedback\nWHERE {string.Join(" AND ", clauses)}\nORDER BY StartTime DESC, FeedbackId\nLIMIT {MaxFeedbackRows + 1}",
            parameters);
    }

    /// <summary>The newest screenshot of one span. Columns: ContentType, ImageBase64.</summary>
    public static AppSessionSql BuildScreenshot(string sessionId, string spanId, DateTimeOffset from, DateTimeOffset to) =>
        BuildScreenshotQuery("SELECT ContentType, ImageBase64", sessionId, spanId, from, to, "ORDER BY StartTime DESC\nLIMIT 1");

    private static AppSessionSql BuildScreenshotQuery(string select, string sessionId, string? spanId, DateTimeOffset from, DateTimeOffset to, string tail, string? extraClause = null)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("from", from.UtcDateTime);
        parameters.AddParameter("to", to.UtcDateTime);
        parameters.AddParameter("sessionId", sessionId);
        var clauses = new List<string>
        {
            "StartTime >= {from:DateTime64(3)}",
            "StartTime < {to:DateTime64(3)}",
            "SessionId = {sessionId:String}",
        };
        if (spanId is not null)
        {
            parameters.AddParameter("spanId", spanId);
            clauses.Add("SpanId = {spanId:String}");
        }
        if (extraClause is not null) clauses.Add(extraClause);
        ServiceScope.Append(clauses, parameters);
        return new AppSessionSql($"{select}\nFROM app_screenshots\nWHERE {string.Join(" AND ", clauses)}\n{tail}".TrimEnd(), parameters);
    }

    private static string Where(ClickHouseParameterCollection parameters, int windowMinutes, DateTimeOffset end, string? service, string? version) =>
        Where(parameters, end.AddMinutes(-windowMinutes), end, service, version);

    private static string Where(ClickHouseParameterCollection parameters, DateTimeOffset from, DateTimeOffset to, string? service, string? version)
    {
        parameters.AddParameter("from", from.UtcDateTime);
        parameters.AddParameter("to", to.UtcDateTime);

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
