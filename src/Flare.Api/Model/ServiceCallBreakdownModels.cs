using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// One <c>peer.service</c> target a service calls out to, aggregated across the window -
/// the "External calls" tab of the Services-tab Map view's per-node drill-down. See
/// <see cref="Query.ServiceCallBreakdownQueryBuilder"/>'s remarks for how it's computed.
/// No <see cref="DateTimeOffset"/>/<see cref="System.Text.Json.JsonElement"/> member, so
/// this carries <c>[GenerateTypeScript]</c> - see <c>Flare.Api.csproj</c>'s MemoryPack
/// TypeScript codegen comment for why that matters.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record ExternalCallGroup
{
    /// <summary>The <c>peer.service</c> span attribute value - the external target's name, as the calling service itself labeled it. Never empty (the query only groups spans that set this attribute).</summary>
    public required string PeerService { get; init; }

    public required ulong CallCount { get; init; }

    public required ulong ErrorCount { get; init; }

    /// <summary><see cref="ErrorCount"/> / <see cref="CallCount"/>, 0.0-1.0 - same "never a real 0/0 guard" reasoning as <see cref="ServiceMetrics.ErrorRate"/>.</summary>
    public required double ErrorRate { get; init; }

    public required double P50DurationMs { get; init; }

    public required double P95DurationMs { get; init; }
}

/// <summary>
/// One <c>db.system</c>/<c>db.operation</c> pair a service issues, aggregated across the
/// window - the "Database" tab of the same per-node drill-down.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record DatabaseCallGroup
{
    /// <summary>The <c>db.system</c> span attribute value (e.g. <c>"postgresql"</c>, <c>"clickhouse"</c>) - never empty (the query only groups spans that set this attribute).</summary>
    public required string DbSystem { get; init; }

    /// <summary>The <c>db.operation</c> span attribute value (e.g. <c>"SELECT"</c>) - empty string when the instrumenting library didn't set it (unlike <see cref="DbSystem"/>, this attribute is optional in the OTel semantic conventions), same empty-string-means-absent convention as <c>ParentSpanId</c>.</summary>
    public required string DbOperation { get; init; }

    public required ulong CallCount { get; init; }

    public required ulong ErrorCount { get; init; }

    public required double ErrorRate { get; init; }

    public required double P50DurationMs { get; init; }

    public required double P95DurationMs { get; init; }
}

/// <summary>
/// Response body for <c>GET /api/services/breakdown</c> - the per-node drill-down opened
/// from clicking a node on the Services tab's Map view, answering "what does this service
/// call, and how slow/erroring is each one" split into external HTTP-ish calls
/// (<see cref="ExternalCalls"/>) and database calls (<see cref="DatabaseCalls"/>).
/// Deliberately hand-written on the MemoryPack TS side (not <c>[GenerateTypeScript]</c>) -
/// same <c>IReadOnlyList&lt;T&gt;</c>-member block as <see cref="ServiceOverviewResponse"/>.
/// </summary>
[MemoryPackable]
public sealed partial record ServiceCallBreakdownResponse
{
    /// <summary>The service this breakdown is for, echoed back from the request - lets the dialog title itself off the response rather than threading the requested name through separately.</summary>
    public required string Service { get; init; }

    public required int WindowMinutes { get; init; }

    /// <summary>Busiest target first - see <see cref="Query.ServiceCallBreakdownQueryBuilder"/>'s external-calls <c>ORDER BY</c>. Empty for a service with no outbound <c>peer.service</c>-tagged spans in the window (including every node that only exists as someone else's <c>peer.service</c> value, not a real instrumented process of its own - see that class's remarks).</summary>
    public required IReadOnlyList<ExternalCallGroup> ExternalCalls { get; init; }

    /// <summary>Busiest db.system/db.operation pair first - see <see cref="Query.ServiceCallBreakdownQueryBuilder"/>'s database-calls <c>ORDER BY</c>.</summary>
    public required IReadOnlyList<DatabaseCallGroup> DatabaseCalls { get; init; }
}
