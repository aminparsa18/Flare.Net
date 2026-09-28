using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT</c> for one of the <c>/external-apis</c> page's queries, ready to hand to <see cref="ExternalApiQueryService"/>.</summary>
public sealed record ExternalApiSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure SQL builder for the <c>/external-apis</c> page (<c>POST /api/external-apis/domains</c>
/// and <c>POST /api/external-apis/domain-detail</c>) - every outbound call our services make,
/// grouped by the domain it went to, derived from client spans' OTel <c>server.address</c>/
/// <c>url.full</c>/<c>http.request.method</c> attributes at query time. No new table; see
/// docs-internal/adr/0071-external-api-monitoring.md.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which spans count.</b> <c>CLIENT</c> spans (<c>Kind = 3</c>) that name a domain and
/// aren't database or messaging calls (<see cref="OutboundCallCondition"/>) - those carry
/// <c>server.address</c> too, but already have their own views (the Services breakdown's
/// Database tab, the <c>/messaging</c> page). The <c>mapContains</c> guard in
/// <see cref="SpanWhere"/> lets <c>idx_span_attr_key</c> (0007_spans.sql) skip granules with
/// none of the address attributes at all.
/// </para>
/// <para>
/// <b>Attribute fallbacks.</b> The domain is <c>server.address</c>, else the pre-1.21
/// <c>net.peer.name</c>, else the host parsed out of the URL (<see cref="DomainExpr"/>); the
/// URL is <c>url.full</c>, else the older <c>http.url</c>; method and status code likewise
/// (<see cref="MethodExpr"/>, <see cref="StatusCodeExpr"/>). .NET instrumentations in the wild
/// still emit either generation.
/// </para>
/// <para>
/// <b>Endpoints.</b> <see cref="EndpointExpr"/> prefers the instrumentation's own
/// <c>url.template</c> (low-cardinality by definition). Without one - the common case for
/// <c>HttpClient</c> - it templates the URL's path itself: any segment that is all digits, a
/// UUID, a long hex string, or 16+ characters containing a digit (<c>cus_Nffr...</c>-style
/// ids) becomes <see cref="IdPlaceholder"/>. A heuristic, so some ids will slip through as
/// their own endpoint; <see cref="EndpointSourceExpr"/> tells the dashboard which rule
/// produced a row so its trace drill-down filters on the right attribute. gRPC calls use
/// <c>rpc.service/rpc.method</c>, anything else its span name.
/// </para>
/// <para>
/// The id pattern and placeholder are bound parameters rather than inline literals - both
/// contain braces, which would read as <c>{name:Type}</c> query parameters.
/// </para>
/// </remarks>
public static class ExternalApiQueryBuilder
{
    public const int DefaultWindowMinutes = 60;
    public const int MinWindowMinutes = 5;
    public const int MaxWindowMinutes = 1440;

    /// <summary>Row cap for the domain list and each detail table. Fetches one more so the caller could tell it was cut.</summary>
    public const int MaxRows = 500;

    /// <summary>Row cap for the detail's top-errors table - the worst few are the point.</summary>
    public const int MaxErrorGroups = 20;

    /// <summary>What an id-like path segment is replaced with in a derived endpoint.</summary>
    public const string IdPlaceholder = "{id}";

    /// <summary>All digits, a UUID, or a 16+ char hex string - the path segments <see cref="EndpointExpr"/> always treats as ids.</summary>
    public const string IdSegmentPattern =
        @"^(\d+|[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|[0-9a-fA-F]{16,})$";

    public const string UrlExpr =
        "if(SpanAttributes['url.full'] != '', SpanAttributes['url.full'], SpanAttributes['http.url'])";

    /// <summary><c>server.address</c>, else <c>net.peer.name</c>, else the URL's host. Also the Services breakdown's External-calls fallback when <c>peer.service</c> is unset (<see cref="ServiceCallBreakdownQueryBuilder.ExternalTargetExpr"/>).</summary>
    public const string DomainExpr =
        "multiIf(SpanAttributes['server.address'] != '', SpanAttributes['server.address'], " +
        "SpanAttributes['net.peer.name'] != '', SpanAttributes['net.peer.name'], " +
        $"domain({UrlExpr}))";

    /// <summary>A client span that isn't a database or messaging call - see the class remarks.</summary>
    public const string OutboundCallCondition =
        "Kind = 3 AND SpanAttributes['db.system'] = '' AND SpanAttributes['messaging.system'] = ''";

    public const string MethodExpr =
        "if(SpanAttributes['http.request.method'] != '', SpanAttributes['http.request.method'], SpanAttributes['http.method'])";

    public const string StatusCodeExpr =
        "if(SpanAttributes['http.response.status_code'] != '', SpanAttributes['http.response.status_code'], SpanAttributes['http.status_code'])";

    /// <summary>
    /// <c>server.port</c>, else <c>net.peer.port</c>, else the URL's explicit port, else the
    /// scheme's default (443/80). Empty when none of those apply (a gRPC call without
    /// <c>server.port</c>, a bare span name).
    /// </summary>
    public const string PortExpr =
        "multiIf(SpanAttributes['server.port'] != '', SpanAttributes['server.port'], " +
        "SpanAttributes['net.peer.port'] != '', SpanAttributes['net.peer.port'], " +
        $"port({UrlExpr}) != 0, toString(port({UrlExpr})), " +
        $"protocol({UrlExpr}) = 'https', '443', " +
        $"protocol({UrlExpr}) = 'http', '80', '')";

    /// <summary>Distinct ports shown per domain - more than a handful means something odd, and the column would just overflow.</summary>
    public const int MaxPortsPerDomain = 5;

    /// <summary>The domain drill-down's charts target roughly this many buckets.</summary>
    private const int TargetBuckets = 60;

    /// <summary>
    /// Bucket width for the drill-down's charts: about <see cref="TargetBuckets"/> buckets,
    /// rounded up to a 10-second multiple so bucket edges land on readable times (5m = 10s,
    /// 1h = 60s, 24h = 24m).
    /// </summary>
    public static int BucketWidthSecondsFor(int windowMinutes) =>
        Math.Max(10, (int)Math.Ceiling(windowMinutes * 60.0 / TargetBuckets / 10.0) * 10);

    /// <summary>Which rule produced <see cref="EndpointExpr"/>'s value - <see cref="ExternalEndpointSource"/>'s ordinals.</summary>
    public const string EndpointSourceExpr =
        "multiIf(SpanAttributes['url.template'] != '', 0, " +
        $"{UrlExpr} != '', 1, " +
        "SpanAttributes['rpc.method'] != '', 2, 3)";

    /// <summary>The URL's path with id-like segments replaced - see the class remarks. Needs <see cref="AddEndpointParameters"/>.</summary>
    private const string TemplatedPathExpr =
        "arrayStringConcat(arrayMap(s -> if(match(s, {idPattern:String}) OR (length(s) >= 16 AND match(s, '[0-9]')), {idPlaceholder:String}, s), " +
        $"splitByChar('/', if(path({UrlExpr}) = '', '/', path({UrlExpr})))), '/')";

    /// <summary>Needs <see cref="AddEndpointParameters"/>.</summary>
    public const string EndpointExpr =
        "multiIf(SpanAttributes['url.template'] != '', SpanAttributes['url.template'], " +
        $"{UrlExpr} != '', {TemplatedPathExpr}, " +
        "SpanAttributes['rpc.method'] != '', concat(SpanAttributes['rpc.service'], '/', SpanAttributes['rpc.method']), " +
        "Name)";

    public static int ClampWindowMinutes(int? requested) =>
        requested is > 0 ? Math.Clamp(requested.Value, MinWindowMinutes, MaxWindowMinutes) : DefaultWindowMinutes;

    /// <summary>Same epoch-ms end convention as <see cref="HostInventoryQueryBuilder.ResolveWindowEnd"/>.</summary>
    public static DateTimeOffset ResolveWindowEnd(long? endUnixMs, DateTimeOffset now) =>
        HostInventoryQueryBuilder.ResolveWindowEnd(endUnixMs, now);

    /// <summary>
    /// One row per domain. Columns: Domain, CallCount, ErrorCount, Quantiles (p50/p95/p99
    /// nanoseconds), ServiceCount, EndpointCount, LastSeenUnixMs, Ports (up to
    /// <see cref="MaxPortsPerDomain"/> distinct <see cref="PortExpr"/> values in numeric order,
    /// joined with <c>", "</c>).
    /// </summary>
    public static ExternalApiSql BuildDomains(ExternalDomainsRequest request, int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = SpanWhere(parameters, windowMinutes, end, request.Service, domain: null);
        AddEndpointParameters(parameters);
        parameters.AddParameter("limit", (uint)(MaxRows + 1));

        var sql = "SELECT\n" +
            "    ExtDomain,\n" +
            "    count() AS CallCount,\n" +
            "    countIf(IsError) AS ErrorCount,\n" +
            "    quantiles(0.5, 0.95, 0.99)(DurationNano) AS Quantiles,\n" +
            "    uniqExact(ServiceName) AS ServiceCount,\n" +
            "    uniqExact(ExtMethod, ExtEndpoint) AS EndpointCount,\n" +
            "    toUnixTimestamp64Milli(max(StartTime)) AS LastSeenUnixMs,\n" +
            $"    arrayStringConcat(arraySort(p -> toUInt32OrZero(p), groupUniqArrayIf({MaxPortsPerDomain})(ExtPort, ExtPort != '')), ', ') AS Ports\n" +
            $"FROM {SpanSource(where, withEndpoint: true)}\n" +
            "GROUP BY ExtDomain\n" +
            "ORDER BY CallCount DESC, ExtDomain\n" +
            "LIMIT {limit:UInt32}";

        return new ExternalApiSql(sql, parameters);
    }

    /// <summary>
    /// Every service that made an outbound call in the window, as one sorted array in one
    /// row - the toolbar's caller picker. Ignores the request's own service filter so picking
    /// one doesn't hide the others.
    /// </summary>
    public static ExternalApiSql BuildFacets(int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = SpanWhere(parameters, windowMinutes, end, service: null, domain: null);

        var sql = "SELECT arraySort(groupUniqArray(1000)(ServiceName)) AS Services\n" +
            $"FROM {SpanSource(where, withEndpoint: false)}";

        return new ExternalApiSql(sql, parameters);
    }

    /// <summary>
    /// One domain's endpoints. Columns: Method, Endpoint, EndpointSource, CallCount,
    /// ErrorCount, Quantiles, LastSeenUnixMs.
    /// </summary>
    public static ExternalApiSql BuildEndpoints(ExternalDomainDetailRequest request, int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = SpanWhere(parameters, windowMinutes, end, request.Service, request.Domain);
        AddEndpointParameters(parameters);
        parameters.AddParameter("limit", (uint)(MaxRows + 1));

        var sql = "SELECT\n" +
            "    ExtMethod,\n" +
            "    ExtEndpoint,\n" +
            "    ExtEndpointSource,\n" +
            "    count() AS CallCount,\n" +
            "    countIf(IsError) AS ErrorCount,\n" +
            "    quantiles(0.5, 0.95, 0.99)(DurationNano) AS Quantiles,\n" +
            "    toUnixTimestamp64Milli(max(StartTime)) AS LastSeenUnixMs\n" +
            $"FROM {SpanSource(where, withEndpoint: true)}\n" +
            "GROUP BY ExtMethod, ExtEndpoint, ExtEndpointSource\n" +
            "ORDER BY CallCount DESC, ExtEndpoint, ExtMethod\n" +
            "LIMIT {limit:UInt32}";

        return new ExternalApiSql(sql, parameters);
    }

    /// <summary>One domain's HTTP response status codes. Columns: StatusCode, CallCount. Spans without one (a connection failure, a non-HTTP call) are left out.</summary>
    public static ExternalApiSql BuildStatusCodes(ExternalDomainDetailRequest request, int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = SpanWhere(parameters, windowMinutes, end, request.Service, request.Domain);
        parameters.AddParameter("limit", (uint)(MaxRows + 1));

        var sql = "SELECT\n" +
            "    ExtStatusCode,\n" +
            "    count() AS CallCount\n" +
            $"FROM {SpanSource(where, withEndpoint: false)}\n" +
            "WHERE ExtStatusCode != ''\n" +
            "GROUP BY ExtStatusCode\n" +
            "ORDER BY toUInt16OrNull(ExtStatusCode) ASC NULLS LAST, ExtStatusCode\n" +
            "LIMIT {limit:UInt32}";

        return new ExternalApiSql(sql, parameters);
    }

    /// <summary>
    /// One domain's calls per <paramref name="bucketWidthSeconds"/> bucket - the drill-down's
    /// rate/error/p95 charts. Columns: BucketStartUnixMs, CallCount, ErrorCount, P95 (nanoseconds).
    /// Buckets with no calls are omitted; the dashboard fills them in.
    /// </summary>
    public static ExternalApiSql BuildSeries(ExternalDomainDetailRequest request, int windowMinutes, int bucketWidthSeconds, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = SpanWhere(parameters, windowMinutes, end, request.Service, request.Domain);
        parameters.AddParameter("bucketWidth", (uint)bucketWidthSeconds);

        var sql = "SELECT\n" +
            "    toInt64(toUnixTimestamp(toStartOfInterval(toDateTime(StartTime), INTERVAL {bucketWidth:UInt32} SECOND))) * 1000 AS BucketStartUnixMs,\n" +
            "    count() AS CallCount,\n" +
            "    countIf(IsError) AS ErrorCount,\n" +
            "    quantile(0.95)(DurationNano) AS P95\n" +
            $"FROM {SpanSource(where, withEndpoint: false)}\n" +
            "GROUP BY BucketStartUnixMs\n" +
            "ORDER BY BucketStartUnixMs";

        return new ExternalApiSql(sql, parameters);
    }

    /// <summary>
    /// The services calling one domain - which of ours depend on it. Columns: ServiceName,
    /// CallCount, ErrorCount, Quantiles.
    /// </summary>
    public static ExternalApiSql BuildCallers(ExternalDomainDetailRequest request, int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = SpanWhere(parameters, windowMinutes, end, request.Service, request.Domain);
        parameters.AddParameter("limit", (uint)(MaxRows + 1));

        var sql = "SELECT\n" +
            "    ServiceName,\n" +
            "    count() AS CallCount,\n" +
            "    countIf(IsError) AS ErrorCount,\n" +
            "    quantiles(0.5, 0.95, 0.99)(DurationNano) AS Quantiles\n" +
            $"FROM {SpanSource(where, withEndpoint: false)}\n" +
            "GROUP BY ServiceName\n" +
            "ORDER BY CallCount DESC, ServiceName\n" +
            "LIMIT {limit:UInt32}";

        return new ExternalApiSql(sql, parameters);
    }

    /// <summary>
    /// One domain's failed calls, grouped by what failed and how: Method, Endpoint,
    /// EndpointSource, StatusCode, ErrorType (<c>error.type</c> - an exception type or the
    /// status code), CallCount, LastSeenUnixMs, SampleMessage (one span's status message).
    /// Worst first, capped at <see cref="MaxErrorGroups"/>.
    /// </summary>
    public static ExternalApiSql BuildTopErrors(ExternalDomainDetailRequest request, int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = SpanWhere(parameters, windowMinutes, end, request.Service, request.Domain);
        AddEndpointParameters(parameters);
        parameters.AddParameter("limit", (uint)MaxErrorGroups);

        var sql = "SELECT\n" +
            "    ExtMethod,\n" +
            "    ExtEndpoint,\n" +
            "    ExtEndpointSource,\n" +
            "    ExtStatusCode,\n" +
            "    ExtErrorType,\n" +
            "    count() AS CallCount,\n" +
            "    toUnixTimestamp64Milli(max(StartTime)) AS LastSeenUnixMs,\n" +
            "    anyIf(StatusMessage, StatusMessage != '') AS SampleMessage\n" +
            $"FROM {SpanSource(where, withEndpoint: true)}\n" +
            "WHERE IsError\n" +
            "GROUP BY ExtMethod, ExtEndpoint, ExtEndpointSource, ExtStatusCode, ExtErrorType\n" +
            "ORDER BY CallCount DESC, LastSeenUnixMs DESC\n" +
            "LIMIT {limit:UInt32}";

        return new ExternalApiSql(sql, parameters);
    }

    /// <summary>
    /// The per-span projection every query here aggregates over. <paramref name="withEndpoint"/>
    /// adds the endpoint columns - left off where they aren't read, since templating a path is
    /// the priciest expression here. The trailing <c>WHERE</c> is the inner query's, so a
    /// caller's own <c>WHERE</c> lands on the outer level.
    /// </summary>
    private static string SpanSource(string where, bool withEndpoint) =>
        "(\n" +
        "    SELECT\n" +
        "        ServiceName,\n" +
        "        StartTime,\n" +
        "        DurationNano,\n" +
        "        StatusMessage,\n" +
        "        StatusCode = 'STATUS_CODE_ERROR' AS IsError,\n" +
        $"        {DomainExpr} AS ExtDomain,\n" +
        $"        {MethodExpr} AS ExtMethod,\n" +
        $"        {StatusCodeExpr} AS ExtStatusCode,\n" +
        $"        {PortExpr} AS ExtPort,\n" +
        (withEndpoint
            ? $"        {EndpointExpr} AS ExtEndpoint,\n" +
              $"        {EndpointSourceExpr} AS ExtEndpointSource,\n"
            : "") +
        "        SpanAttributes['error.type'] AS ExtErrorType\n" +
        "    FROM spans\n" +
        $"    WHERE {where}\n" +
        ")";

    private static string SpanWhere(ClickHouseParameterCollection parameters, int windowMinutes, DateTimeOffset end, string? service, string? domain)
    {
        parameters.AddParameter("from", end.AddMinutes(-windowMinutes).UtcDateTime);
        parameters.AddParameter("to", end.UtcDateTime);

        var clauses = new List<string>
        {
            "StartTime >= {from:DateTime64(9)}",
            "StartTime < {to:DateTime64(9)}",
            "(mapContains(SpanAttributes, 'server.address') OR mapContains(SpanAttributes, 'net.peer.name') " +
                "OR mapContains(SpanAttributes, 'url.full') OR mapContains(SpanAttributes, 'http.url'))",
            OutboundCallCondition,
            $"{DomainExpr} != ''",
        };

        if (!string.IsNullOrWhiteSpace(service))
        {
            parameters.AddParameter("service", service);
            clauses.Add("ServiceName = {service:String}");
        }

        if (domain is not null)
        {
            parameters.AddParameter("domain", domain);
            clauses.Add($"{DomainExpr} = {{domain:String}}");
        }

        return string.Join(" AND ", clauses);
    }

    private static void AddEndpointParameters(ClickHouseParameterCollection parameters)
    {
        parameters.AddParameter("idPattern", IdSegmentPattern);
        parameters.AddParameter("idPlaceholder", IdPlaceholder);
    }
}
