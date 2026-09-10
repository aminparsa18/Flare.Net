using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>A parameterized external-calls+database-calls query pair plus their bound parameters (one collection per query - same reasoning as <see cref="ServiceDependencyGraphSql"/>).</summary>
public sealed record ServiceCallBreakdownSql(
    string ExternalCallsSql,
    ClickHouseParameterCollection ExternalCallsParameters,
    string DatabaseCallsSql,
    ClickHouseParameterCollection DatabaseCallsParameters);

/// <summary>
/// Pure (service, window) → parameterized SQL builder for the Services tab Map view's
/// per-node drill-down - "what does this service call, and how slow/erroring is each
/// one," split by <c>peer.service</c> (external calls) and <c>db.system</c>/<c>db.operation</c>
/// (database calls), per docs-internal/planning/roadmap.md's now-removed "Global service
/// map" item. Same "pure function, no ClickHouse dependency" style as
/// <see cref="ServiceDependencyQueryBuilder"/>, split out of
/// <see cref="ServiceCallBreakdownQueryService"/> so both SQL shapes are unit-testable on
/// their own.
/// </summary>
/// <remarks>
/// Both queries filter on the literal <c>ServiceName</c> column, not the <c>peer.service</c>-
/// override "effective service" <see cref="ServiceDependencyQueryBuilder"/>'s node/edge
/// queries key by - this drill-down answers "what does the real process behind this node
/// actually call," which only makes sense read off spans that process genuinely emitted
/// itself. For the common case (a real separately-instrumented downstream service) the two
/// notions coincide anyway, since such a span's own <c>ServiceName</c> already equals its
/// effective service. For a purely virtual node - one that only exists in the graph
/// because some other span's <c>peer.service</c> named it, with no separately-instrumented
/// process of its own ever reporting spans under that name - this correctly comes back
/// empty rather than fabricating a breakdown for a system Flare has no visibility into.
/// <para>
/// <b>External calls</b>: every span this service emitted that carries a non-empty
/// <c>peer.service</c> attribute, grouped by that attribute's value. <b>Database calls</b>:
/// every span this service emitted that carries a non-empty <c>db.system</c> attribute,
/// grouped by <c>(db.system, db.operation)</c> - <c>db.operation</c> is optional in the OTel
/// semantic conventions (unlike <c>db.system</c>), so it can legitimately group as an empty
/// string when a span sets the system but not the operation. A span could in principle
/// carry both attributes at once (an HTTP call to a hosted database's REST API, say); both
/// queries would then count it once each, in their own tab - deliberately not
/// mutually exclusive, since "did this get counted as an external call" and "did this get
/// counted as a database call" are independent questions this drill-down answers
/// separately, not a single classification each span gets exactly one of.
/// </para>
/// <para>
/// <b>No index helps either query</b> - same unfiltered-by-window-only tradeoff as
/// <see cref="ServiceOverviewQueryBuilder"/>'s remarks, except here <c>ServiceName</c>
/// *is* an equality filter, so <c>0007_spans.sql</c>'s <c>idx_service</c> bloom filter skip
/// index actually prunes granules for it - materially cheaper than
/// <see cref="ServiceDependencyQueryBuilder"/>'s unfiltered-by-service aggregates.
/// </para>
/// </remarks>
public static class ServiceCallBreakdownQueryBuilder
{
    public const int DefaultWindowMinutes = ServiceOverviewQueryBuilder.DefaultWindowMinutes;
    public const int MinWindowMinutes = ServiceOverviewQueryBuilder.MinWindowMinutes;
    public const int MaxWindowMinutes = ServiceOverviewQueryBuilder.MaxWindowMinutes;

    /// <summary>Same clamp as <see cref="ServiceOverviewQueryBuilder.ClampWindowMinutes"/> - kept as its own method so this builder reads standalone, same precedent as <see cref="ServiceDependencyQueryBuilder.ClampWindowMinutes"/>.</summary>
    public static int ClampWindowMinutes(int requested) => ServiceOverviewQueryBuilder.ClampWindowMinutes(requested);

    /// <param name="resourceAttributes">
    /// Optional equality filters against <c>ResourceAttributes</c> - the Services tab's
    /// filter chips, ANDed into both queries alongside the service/window predicates.
    /// Null/empty = no narrowing, same queries as before this parameter existed. See
    /// <see cref="ResourceAttributeFilterSqlBuilder"/>.
    /// </param>
    public static ServiceCallBreakdownSql Build(string service, TimeSpan window, DateTimeOffset now, IReadOnlyList<ResourceAttributeFilter>? resourceAttributes = null)
    {
        var externalCallsParameters = new ClickHouseParameterCollection();
        externalCallsParameters.AddParameter("service", service);
        externalCallsParameters.AddParameter("from", (now - window).UtcDateTime);
        externalCallsParameters.AddParameter("to", now.UtcDateTime);
        externalCallsParameters.AddParameter("errorStatus", "STATUS_CODE_ERROR");

        var externalCallsClauses = new List<string>
        {
            "ServiceName = {service:String}",
            "SpanAttributes['peer.service'] != ''",
            "StartTime >= {from:DateTime64(9)} AND StartTime < {to:DateTime64(9)}",
        };
        ResourceAttributeFilterSqlBuilder.AppendClauses(externalCallsClauses, externalCallsParameters, resourceAttributes, columnAlias: string.Empty, paramPrefix: string.Empty);

        var externalCallsSql = "SELECT\n" +
            "    SpanAttributes['peer.service'] AS PeerService,\n" +
            "    count() AS CallCount,\n" +
            "    countIf(StatusCode = {errorStatus:String}) AS ErrorCount,\n" +
            "    quantile(0.5)(DurationNano) AS P50DurationNano,\n" +
            "    quantile(0.95)(DurationNano) AS P95DurationNano\n" +
            "FROM spans\n" +
            "WHERE " + string.Join(" AND ", externalCallsClauses) + "\n" +
            "GROUP BY PeerService\n" +
            "ORDER BY CallCount DESC";

        var databaseCallsParameters = new ClickHouseParameterCollection();
        databaseCallsParameters.AddParameter("service", service);
        databaseCallsParameters.AddParameter("from", (now - window).UtcDateTime);
        databaseCallsParameters.AddParameter("to", now.UtcDateTime);
        databaseCallsParameters.AddParameter("errorStatus", "STATUS_CODE_ERROR");

        var databaseCallsClauses = new List<string>
        {
            "ServiceName = {service:String}",
            "SpanAttributes['db.system'] != ''",
            "StartTime >= {from:DateTime64(9)} AND StartTime < {to:DateTime64(9)}",
        };
        ResourceAttributeFilterSqlBuilder.AppendClauses(databaseCallsClauses, databaseCallsParameters, resourceAttributes, columnAlias: string.Empty, paramPrefix: string.Empty);

        var databaseCallsSql = "SELECT\n" +
            "    SpanAttributes['db.system'] AS DbSystem,\n" +
            "    SpanAttributes['db.operation'] AS DbOperation,\n" +
            "    count() AS CallCount,\n" +
            "    countIf(StatusCode = {errorStatus:String}) AS ErrorCount,\n" +
            "    quantile(0.5)(DurationNano) AS P50DurationNano,\n" +
            "    quantile(0.95)(DurationNano) AS P95DurationNano\n" +
            "FROM spans\n" +
            "WHERE " + string.Join(" AND ", databaseCallsClauses) + "\n" +
            "GROUP BY DbSystem, DbOperation\n" +
            "ORDER BY CallCount DESC";

        return new ServiceCallBreakdownSql(externalCallsSql, externalCallsParameters, databaseCallsSql, databaseCallsParameters);
    }
}
