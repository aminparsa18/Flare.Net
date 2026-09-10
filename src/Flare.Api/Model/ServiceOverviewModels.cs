using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Per-service RED metrics (Rate/Errors/Duration) for one window, aggregated from
/// <c>spans</c> - the row shape behind <c>GET /api/services/overview</c>, the
/// dashboard's "Services" tab on the Traces page (see
/// docs-internal/planning/roadmap.md's now-removed "Per-service RED-metrics overview"
/// item). No <see cref="DateTimeOffset"/>/<see cref="System.Text.Json.JsonElement"/>
/// member, so this carries <c>[GenerateTypeScript]</c> - see
/// <c>Flare.Api.csproj</c>'s MemoryPack TypeScript codegen comment for why that matters.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record ServiceMetrics
{
    public required string ServiceName { get; init; }

    /// <summary>Root spans (<c>ParentSpanId = ''</c>) started in the window - see <see cref="Query.ServiceOverviewQueryBuilder"/>'s remarks for why root spans stand in for "requests."</summary>
    public required ulong RequestCount { get; init; }

    /// <summary>Of <see cref="RequestCount"/>, how many carried <c>StatusCode = STATUS_CODE_ERROR</c>.</summary>
    public required ulong ErrorCount { get; init; }

    /// <summary><see cref="ErrorCount"/> / <see cref="RequestCount"/>, 0.0-1.0. Always 0 when <see cref="RequestCount"/> is 0 (a service can't appear in the response at all without at least one request - see the query builder's <c>GROUP BY</c> - so this is only ever a real division, never a 0/0 guard).</summary>
    public required double ErrorRate { get; init; }

    /// <summary><see cref="RequestCount"/> divided by the window length in seconds.</summary>
    public required double RequestsPerSecond { get; init; }

    /// <summary>Median root-span duration, milliseconds. ClickHouse's <c>quantile()</c> - approximate (t-digest), same tradeoff as every other percentile this codebase surfaces (see <c>HistogramQuantileEstimator</c>'s own remarks).</summary>
    public required double P50DurationMs { get; init; }

    public required double P95DurationMs { get; init; }

    public required double P99DurationMs { get; init; }
}

/// <summary>
/// Response body for <c>POST /api/services/overview</c>. Deliberately hand-written on
/// the MemoryPack TS side (not <c>[GenerateTypeScript]</c>) - an
/// <c>IReadOnlyList&lt;ServiceMetrics&gt;</c> member blocks the generator the same way
/// <c>IndexingStatsResponse</c>'s list members do; see <c>indexing-api.ts</c>'s header
/// comment for the precedent.
/// </summary>
[MemoryPackable]
public sealed partial record ServiceOverviewResponse
{
    /// <summary>The window this snapshot covers, as requested (post-<see cref="Query.ServiceOverviewQueryBuilder.ClampWindowMinutes"/> clamp).</summary>
    public required int WindowMinutes { get; init; }

    /// <summary>Highest <see cref="ServiceMetrics.RequestCount"/> first - see <see cref="Query.ServiceOverviewQueryBuilder"/>'s <c>ORDER BY</c>. The dashboard table re-sorts client-side on any column; this default just makes the unsorted table land on the busiest, most likely-relevant services first.</summary>
    public required IReadOnlyList<ServiceMetrics> Services { get; init; }
}

/// <summary>
/// Request body for <c>POST /api/services/overview</c> - was a plain
/// <c>?windowMinutes=</c> query-string param until the Services tab's resource-attribute
/// filter chips (docs-internal/planning/roadmap.md's now-removed "Resource-attribute
/// filtering on the Traces &gt; Services tab" item) made the request structured/multi-valued,
/// the same "filters are multi-valued/structured, so POST not GET-with-query-string"
/// reasoning this codebase's CLAUDE.md already documents for <c>/api/logs/*</c>. Flat
/// fields rather than a nested nested filter object - unlike <see cref="LogSearchRequest.Filter"/>,
/// there's no third field these two would otherwise collide with, so a wrapper type would
/// only add System.Text.Json's documented init-property-default-nulling gotcha
/// (<see cref="LogSearchRequest.Filter"/>'s own remarks) for no benefit.
/// </summary>
[MemoryPackable]
public sealed partial record ServiceOverviewRequest
{
    /// <summary>Lookback window, minutes. Null/non-positive defaults - see <see cref="Query.ServiceOverviewQueryBuilder.ClampWindowMinutes"/>.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Equality filters against <c>ResourceAttributes</c>, ANDed together - the Services tab's filter chips. Null/empty = no narrowing.</summary>
    public IReadOnlyList<ResourceAttributeFilter>? ResourceAttributes { get; init; }
}
