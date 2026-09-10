using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// One service, aggregated across every span attributed to it (via <c>peer.service</c>
/// override, else its own <c>ServiceName</c>) in the window - the aggregate-graph sibling
/// of the dashboard's per-trace <c>ServiceMapNode</c> (<c>service-map.ts</c>), same field
/// shape on purpose so a future dashboard component can render both. Backs the "Map" view
/// of <c>GET /api/services/dependencies</c> - see <see cref="Query.ServiceDependencyQueryBuilder"/>'s
/// remarks for how it's computed. Deliberately hand-written on the MemoryPack TS side (not
/// <c>[GenerateTypeScript]</c>) - <see cref="TopOperations"/>'s <c>IReadOnlyList&lt;string&gt;</c>
/// blocks the generator (MEMPACK031: no list-of-primitive member is a supported
/// TypeScript-generation type, not just list-of-object ones like
/// <see cref="ServiceOverviewResponse.Services"/>).
/// </summary>
[MemoryPackable]
public sealed partial record ServiceDependencyNode
{
    /// <summary>The <c>peer.service</c>-overridden or actual service name - see <see cref="Query.ServiceDependencyQueryBuilder"/>'s <c>EffectiveServiceExpr</c>. May name a service with no spans of its own in this store (an uninstrumented external dependency only ever seen as a caller's <c>peer.service</c> attribute).</summary>
    public required string Service { get; init; }

    /// <summary>Every span attributed to this service in the window - not just root spans, unlike <see cref="ServiceMetrics.RequestCount"/> (this counts "how much work," not "how many inbound requests").</summary>
    public required ulong SpanCount { get; init; }

    /// <summary>Of <see cref="SpanCount"/>, how many carried <c>StatusCode = STATUS_CODE_ERROR</c>.</summary>
    public required ulong ErrorCount { get; init; }

    /// <summary>Sum of <c>DurationNano</c> across every span attributed to this service - "how much work this service did" across every trace in the window, not a latency percentile (spans overlap and vary wildly in shape at this aggregate level, so a sum stands in the same way it does in <c>service-map.ts</c>'s single-trace node, not <see cref="ServiceMetrics"/>'s per-request quantiles).</summary>
    public required ulong TotalDurationNano { get; init; }

    /// <summary>Up to 3 of this service's most frequent span names in the window, via ClickHouse's <c>topK(3)</c> - an approximate sketch, not an exact top-3 (same accepted-approximation precedent as this codebase's <c>quantile()</c> percentiles), standing in for <c>service-map.ts</c>'s exact-but-per-trace "first-seen operations" list.</summary>
    public required IReadOnlyList<string> TopOperations { get; init; }
}

/// <summary>
/// One directed service-to-service call relationship, aggregated across every trace in
/// the window - the aggregate-graph sibling of <c>service-map.ts</c>'s <c>ServiceMapEdge</c>,
/// same field shape on purpose. Computed by self-joining <c>spans</c> to its own parent
/// row - see <see cref="Query.ServiceDependencyQueryBuilder"/>'s remarks.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record ServiceDependencyEdge
{
    public required string Source { get; init; }

    public required string Target { get; init; }

    /// <summary>Spans crossing from <see cref="Source"/> into <see cref="Target"/> across every trace in the window - one edge can represent many calls, the same way one call graph edge in a single trace can (a loop, a burst-generated repeat), just summed over many traces instead of one.</summary>
    public required ulong CallCount { get; init; }

    /// <summary>Sum of <c>DurationNano</c> across every crossing span - the calling span's own duration is the natural stand-in for "how long this call took," same convention as <c>ServiceMapEdge.totalDurationNano</c>.</summary>
    public required ulong TotalDurationNano { get; init; }
}

/// <summary>
/// Response body for <c>POST /api/services/dependencies</c>. Deliberately hand-written on
/// the MemoryPack TS side (not <c>[GenerateTypeScript]</c>) - an
/// <c>IReadOnlyList&lt;T&gt;</c> member blocks the generator the same way
/// <see cref="ServiceOverviewResponse"/>'s own list member does; see <c>indexing-api.ts</c>'s
/// header comment for the precedent.
/// </summary>
[MemoryPackable]
public sealed partial record ServiceDependencyGraphResponse
{
    /// <summary>The window this snapshot covers, as requested (post-<see cref="Query.ServiceDependencyQueryBuilder.ClampWindowMinutes"/> clamp) - same shared clamp as <see cref="ServiceOverviewResponse.WindowMinutes"/>, so switching between the Services tab's Table and Map views can reuse one window control.</summary>
    public required int WindowMinutes { get; init; }

    /// <summary>Busiest service (by <see cref="ServiceDependencyNode.SpanCount"/>) first - see <see cref="Query.ServiceDependencyQueryBuilder"/>'s nodes <c>ORDER BY</c>.</summary>
    public required IReadOnlyList<ServiceDependencyNode> Nodes { get; init; }

    /// <summary>Busiest edge (by <see cref="ServiceDependencyEdge.CallCount"/>) first - see <see cref="Query.ServiceDependencyQueryBuilder"/>'s edges <c>ORDER BY</c>.</summary>
    public required IReadOnlyList<ServiceDependencyEdge> Edges { get; init; }
}

/// <summary>Request body for <c>POST /api/services/dependencies</c> - see <see cref="ServiceOverviewRequest"/>'s remarks for why this is POST-with-a-flat-body rather than the GET-with-query-string it used to be.</summary>
[MemoryPackable]
public sealed partial record ServiceDependencyRequest
{
    /// <summary>Lookback window, minutes. Null/non-positive defaults - see <see cref="Query.ServiceDependencyQueryBuilder.ClampWindowMinutes"/>.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Equality filters against <c>ResourceAttributes</c>, ANDed together - the Services tab's filter chips. Null/empty = no narrowing. See <see cref="Query.ServiceDependencyQueryBuilder"/>'s remarks on how this is applied to the edges query's two aliased sides.</summary>
    public IReadOnlyList<ResourceAttributeFilter>? ResourceAttributes { get; init; }
}
