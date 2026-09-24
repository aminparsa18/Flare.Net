using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>A parameterized nodes+edges query pair plus their bound parameters (one collection per query - see this class's remarks on why they aren't shared).</summary>
public sealed record ServiceDependencyGraphSql(
    string NodesSql,
    ClickHouseParameterCollection NodesParameters,
    string EdgesSql,
    ClickHouseParameterCollection EdgesParameters);

/// <summary>
/// Pure window → parameterized SQL builder for the Traces page's Services-tab "Map" view -
/// the aggregate, cross-trace counterpart to the dashboard's per-trace
/// <c>service-map.ts</c>'s <c>buildServiceMap()</c>. Same "pure function, no ClickHouse
/// dependency" style as <see cref="ServiceOverviewQueryBuilder"/>, split out of
/// <see cref="ServiceDependencyQueryService"/> so both SQL shapes are unit-testable on
/// their own.
/// </summary>
/// <remarks>
/// <c>service-map.ts</c> builds one trace's graph in memory: walk that trace's spans,
/// attribute each to <c>effectiveService(span)</c> (its <c>peer.service</c> span attribute
/// override, else its own <c>ServiceName</c> resource attribute), and draw an edge from a
/// span's parent's effective service to its own whenever they differ. This builder
/// produces the exact same shape - <see cref="ServiceDependencyNode"/>/<see cref="ServiceDependencyEdge"/>
/// mirror <c>ServiceMapNode</c>/<c>ServiceMapEdge</c> field-for-field on purpose, so the
/// dashboard can eventually reuse <c>ServiceMapNode.svelte</c>'s rendering for both views -
/// but does it as two SQL aggregates over every trace in the window instead of one
/// in-memory walk over one trace's spans, since loading every span of every trace in a
/// window to the client the way a single trace's waterfall does would not scale.
/// <para>
/// <b>Nodes</b>: a plain <c>GROUP BY</c> over every span in the window, keyed by the same
/// <c>effectiveService</c> expression (<see cref="EffectiveServiceExpr"/>) applied to that
/// span itself - no join needed, since a span's own attributes are enough to attribute it.
/// <c>topK(3)</c> stands in for <c>service-map.ts</c>'s "first-seen, capped" operations
/// list - approximate (a sketch, not exact top-3), same accepted-approximation precedent
/// as this codebase's <c>quantile()</c> percentiles, and the cap keeps a busy service's
/// long tail of distinct operation names from ballooning the response the way
/// <c>ServiceMapNode.svelte</c>'s own <c>OPERATIONS_SHOWN</c> cap keeps it off the card.
/// </para>
/// <para>
/// <b>Edges</b>: a span's parent lives in the same <c>spans</c> table, so finding "who
/// called this span" cross-trace means self-joining <c>spans</c> to itself on
/// <c>(TraceId, ParentSpanId) = (TraceId, SpanId)</c> - <c>effectiveService</c> applied to
/// both sides, grouped, same-service pairs dropped (a call one service makes to its own
/// other spans isn't a dependency edge). Two separate <see cref="ClickHouseParameterCollection"/>
/// instances rather than one shared - a driver-bound parameter collection is scoped to the
/// single command it's handed to in <see cref="ServiceDependencyQueryService"/>, so reusing
/// one across two separate <c>ExecuteReaderAsync</c> calls risks the second query
/// double-consuming or re-binding parameters meant for the first (both queries bind the
/// same <c>from</c>/<c>to</c> names, which would otherwise collide).
/// </para>
/// <para>
/// <b>Window on both sides of the edges join</b>: the child span must start inside the
/// window; its parent must start inside the window widened by <see cref="ParentStartSlack"/>
/// at the <c>from</c> end - a parent that started a moment before <c>from</c> but whose
/// child crossed into the window still counts (the same "count the response, not the exact
/// request start" latitude <see cref="ServiceOverviewQueryBuilder"/>'s root-spans
/// convention already takes). The parent bound isn't there for correctness - it's what
/// lets the parent side use migration 0025's <c>StartTime</c>-ordered
/// <c>spans_by_start_time</c> projection instead of reading the whole table to build the
/// join's hash table (<c>spans</c> itself is <c>TraceId</c>-first, so no <c>StartTime</c>
/// predicate prunes it). Accepted cost: a call whose parent started more than
/// <see cref="ParentStartSlack"/> before the window no longer draws an edge. See ADR-0043.
/// </para>
/// <para>
/// <b>The nodes query's live path still has no index</b> - same unfiltered-by-service
/// tradeoff <see cref="ServiceOverviewQueryBuilder"/>'s remarks document. It only runs when
/// ADR-0031's pre-aggregated path is off or a resource-attribute chip is present. The
/// projection deliberately omits <c>ResourceAttributes</c> (see 0025's remarks), so a
/// chip-filtered edges query falls back to the base table too - correct, just not faster.
/// </para>
/// </remarks>
public static class ServiceDependencyQueryBuilder
{
    public const int DefaultWindowMinutes = ServiceOverviewQueryBuilder.DefaultWindowMinutes;
    public const int MinWindowMinutes = ServiceOverviewQueryBuilder.MinWindowMinutes;
    public const int MaxWindowMinutes = ServiceOverviewQueryBuilder.MaxWindowMinutes;

    /// <summary>Same clamp as <see cref="ServiceOverviewQueryBuilder.ClampWindowMinutes"/> - kept as its own method (rather than callers reaching into that class) so this builder reads standalone.</summary>
    public static int ClampWindowMinutes(int requested) => ServiceOverviewQueryBuilder.ClampWindowMinutes(requested);

    /// <summary>How far before the window's <c>from</c> an edge's parent span may have started and still count - see this class's remarks on why the parent side is bounded at all.</summary>
    public static readonly TimeSpan ParentStartSlack = TimeSpan.FromHours(1);

    /// <summary><c>peer.service</c> override, else the span's own <c>ServiceName</c> - the SQL form of <c>service-map.ts</c>'s <c>effectiveService()</c>. <paramref name="alias"/> is the table alias/prefix (empty for the unqualified nodes query, <c>"parent."</c>/<c>"child."</c> for the self-joined edges query).</summary>
    private static string EffectiveServiceExpr(string alias) =>
        $"if({alias}SpanAttributes['peer.service'] != '', {alias}SpanAttributes['peer.service'], {alias}ServiceName)";

    /// <param name="resourceAttributes">
    /// Optional equality filters against <c>ResourceAttributes</c> - the Services tab's
    /// filter chips. Applied unqualified to the nodes query; applied to <b>both</b> the
    /// <c>parent.</c> and <c>child.</c> sides of the edges query (unlike the window
    /// predicate's documented child-only latitude above) - a chip like
    /// <c>deployment.environment=production</c> means "show only what's running in
    /// production," and an edge whose caller matched but whose callee didn't (or vice
    /// versa) would misrepresent that. Null/empty = no narrowing, same queries as before
    /// this parameter existed. See <see cref="ResourceAttributeFilterSqlBuilder"/>.
    /// </param>
    public static ServiceDependencyGraphSql Build(TimeSpan window, DateTimeOffset now, IReadOnlyList<ResourceAttributeFilter>? resourceAttributes = null)
    {
        var nodesParameters = new ClickHouseParameterCollection();
        nodesParameters.AddParameter("from", (now - window).UtcDateTime);
        nodesParameters.AddParameter("to", now.UtcDateTime);
        nodesParameters.AddParameter("errorStatus", "STATUS_CODE_ERROR");

        var nodesClauses = new List<string> { "StartTime >= {from:DateTime64(9)} AND StartTime < {to:DateTime64(9)}" };
        ResourceAttributeFilterSqlBuilder.AppendClauses(nodesClauses, nodesParameters, resourceAttributes, columnAlias: string.Empty, paramPrefix: string.Empty);

        var nodesSql = "SELECT\n" +
            $"    {EffectiveServiceExpr(string.Empty)} AS Service,\n" +
            "    count() AS SpanCount,\n" +
            "    countIf(StatusCode = {errorStatus:String}) AS ErrorCount,\n" +
            "    sum(DurationNano) AS TotalDurationNano,\n" +
            "    topK(3)(Name) AS TopOperations\n" +
            "FROM spans\n" +
            "WHERE " + string.Join(" AND ", nodesClauses) + "\n" +
            "GROUP BY Service\n" +
            "ORDER BY SpanCount DESC";

        var edgesParameters = new ClickHouseParameterCollection();
        edgesParameters.AddParameter("from", (now - window).UtcDateTime);
        edgesParameters.AddParameter("to", now.UtcDateTime);

        edgesParameters.AddParameter("parentFrom", (now - window - ParentStartSlack).UtcDateTime);

        var edgesClauses = new List<string>
        {
            "child.StartTime >= {from:DateTime64(9)} AND child.StartTime < {to:DateTime64(9)}",
            "child.ParentSpanId != ''",
            "parent.StartTime >= {parentFrom:DateTime64(9)} AND parent.StartTime < {to:DateTime64(9)}",
        };
        ResourceAttributeFilterSqlBuilder.AppendClauses(edgesClauses, edgesParameters, resourceAttributes, columnAlias: "parent.", paramPrefix: "parent");
        ResourceAttributeFilterSqlBuilder.AppendClauses(edgesClauses, edgesParameters, resourceAttributes, columnAlias: "child.", paramPrefix: "child");

        var edgesSql = "SELECT\n" +
            $"    {EffectiveServiceExpr("parent.")} AS Source,\n" +
            $"    {EffectiveServiceExpr("child.")} AS Target,\n" +
            "    count() AS CallCount,\n" +
            "    sum(child.DurationNano) AS TotalDurationNano\n" +
            "FROM spans AS child\n" +
            "INNER JOIN spans AS parent ON parent.TraceId = child.TraceId AND parent.SpanId = child.ParentSpanId\n" +
            "WHERE " + string.Join(" AND ", edgesClauses) + "\n" +
            "GROUP BY Source, Target\n" +
            "HAVING Source != Target\n" +
            "ORDER BY CallCount DESC";

        return new ServiceDependencyGraphSql(nodesSql, nodesParameters, edgesSql, edgesParameters);
    }
}
