using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// One step of a trace funnel: which spans count as "the trace reached this step". Every
/// set condition must hold (AND); at least one must be set. See
/// docs-internal/adr/0067-trace-funnels.md.
/// </summary>
[MemoryPackable]
public sealed partial record TraceFunnelStep
{
    /// <summary>Exact <c>ServiceName</c>. Null/empty = any service.</summary>
    public string? ServiceName { get; init; }

    /// <summary>Exact span name (the <c>Name</c> column, the operation). Null/empty = any name.</summary>
    public string? SpanName { get; init; }

    /// <summary>Same filters, operators and semantics as <see cref="SpanFilter.Attributes"/>, ANDed together.</summary>
    public IReadOnlyList<SpanAttributeFilter>? Attributes { get; init; }
}

/// <summary>Request body for <c>POST /api/traces/funnel</c>.</summary>
[MemoryPackable]
public sealed partial record TraceFunnelRequest
{
    /// <summary>Lookback window; null/non-positive = <see cref="Query.TraceFunnelQueryBuilder.DefaultWindowMinutes"/>, clamped server-side.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Where the window ends, as Unix epoch milliseconds; null = now. Same convention as <see cref="MessagingDestinationsRequest.EndUnixMs"/>.</summary>
    public long? EndUnixMs { get; init; }

    /// <summary>
    /// The ordered steps, <see cref="Query.TraceFunnelQueryBuilder.MinSteps"/> to
    /// <see cref="Query.TraceFunnelQueryBuilder.MaxSteps"/> of them.
    /// </summary>
    public IReadOnlyList<TraceFunnelStep>? Steps { get; init; }
}

/// <summary>
/// One step's figures. <see cref="TraceCount"/> is cumulative: a trace counts here only if
/// it also reached every earlier step, in order. The latency fields describe the transition
/// <em>into</em> this step from the previous one (start of the previous step's span to start
/// of this step's span), so they are 0 for the first step.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record TraceFunnelStepResult
{
    public required ulong TraceCount { get; init; }

    /// <summary>Traces whose span for this step (the one the funnel matched) has status <c>ERROR</c>.</summary>
    public required ulong ErrorCount { get; init; }

    public required double AvgTransitionMs { get; init; }

    public required double P50TransitionMs { get; init; }

    public required double P95TransitionMs { get; init; }

    public required double P99TransitionMs { get; init; }
}

/// <summary>Response body for <c>POST /api/traces/funnel</c>.</summary>
[MemoryPackable]
public sealed partial record TraceFunnelResponse
{
    public required int WindowMinutes { get; init; }

    /// <summary>One entry per request step, same order.</summary>
    public required IReadOnlyList<TraceFunnelStepResult> Steps { get; init; }
}

/// <summary>Which traces <c>POST /api/traces/funnel/traces</c> lists for a step.</summary>
public enum TraceFunnelOutcome
{
    /// <summary>Reached the step (and every earlier one).</summary>
    Reached,

    /// <summary>Reached the step but not the next one - the drop-off. Invalid for the last step.</summary>
    Dropped,

    /// <summary>Reached the step, and its matched span has status <c>ERROR</c>.</summary>
    Errored,
}

/// <summary>Request body for <c>POST /api/traces/funnel/traces</c> - the drill-down from one step.</summary>
[MemoryPackable]
public sealed partial record TraceFunnelTracesRequest
{
    public int? WindowMinutes { get; init; }

    public long? EndUnixMs { get; init; }

    public IReadOnlyList<TraceFunnelStep>? Steps { get; init; }

    /// <summary>Zero-based index into <see cref="Steps"/>.</summary>
    public int StepIndex { get; init; }

    public TraceFunnelOutcome Outcome { get; init; } = TraceFunnelOutcome.Dropped;
}

/// <summary>One trace in a funnel drill-down.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record TraceFunnelTrace
{
    public required string TraceId { get; init; }

    /// <summary>Start of the trace's first-step span, Unix epoch milliseconds.</summary>
    public required long StartUnixMs { get; init; }

    /// <summary>How many steps the trace reached, 1-based (1 = entered the funnel only).</summary>
    public required int ReachedSteps { get; init; }

    /// <summary>First-step span start to last-reached-step span start, milliseconds. 0 when only the first step was reached.</summary>
    public required double ElapsedMs { get; init; }
}

/// <summary>Response body for <c>POST /api/traces/funnel/traces</c>.</summary>
[MemoryPackable]
public sealed partial record TraceFunnelTracesResponse
{
    /// <summary>Most recent first, capped at <see cref="Query.TraceFunnelQueryBuilder.MaxTraces"/>.</summary>
    public required IReadOnlyList<TraceFunnelTrace> Traces { get; init; }
}
