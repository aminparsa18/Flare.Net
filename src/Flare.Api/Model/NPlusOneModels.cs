using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/traces/n-plus-one</c> - the "worst offenders" list: database
/// statements that one parent span issued over and over within a single trace (the classic
/// N+1 pattern). See <see cref="Query.NPlusOneQueryBuilder"/>.
/// </summary>
[MemoryPackable]
public sealed partial record NPlusOneRequest
{
    /// <summary>Lookback window; null/non-positive = <see cref="Query.NPlusOneQueryBuilder.DefaultWindowMinutes"/>, clamped server-side.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Where the window ends, as Unix epoch milliseconds; null = now.</summary>
    public long? EndUnixMs { get; init; }

    /// <summary>Exact <c>ServiceName</c> of the calling span. Null/empty = all services.</summary>
    public string? Service { get; init; }

    /// <summary>Same statement repeated at least this many times under one parent span; null = <see cref="Query.NPlusOneQueryBuilder.DefaultMinRepeats"/>, clamped.</summary>
    public int? MinRepeats { get; init; }
}

/// <summary>One repeated statement for one service, aggregated over every trace in the window that showed the pattern.</summary>
[MemoryPackable]
public sealed partial record NPlusOneOffender
{
    public required string ServiceName { get; init; }

    /// <summary>The normalized statement (literals replaced by <c>?</c>), or <c>operation collection</c> when the span carried no query text.</summary>
    public required string Statement { get; init; }

    /// <summary>Traces in which some parent span repeated the statement at least the minimum number of times.</summary>
    public required ulong TraceCount { get; init; }

    /// <summary>Highest repeat count under a single parent span.</summary>
    public required ulong MaxRepeats { get; init; }

    /// <summary>Total statement executions across all offending parents.</summary>
    public required ulong TotalRepeats { get; init; }

    /// <summary>Summed duration of those executions, milliseconds.</summary>
    public required double TotalDurationMs { get; init; }

    /// <summary>A trace showing the worst case, for the drill-down link.</summary>
    public required string ExampleTraceId { get; init; }
}

/// <summary>Response body for <c>POST /api/traces/n-plus-one</c>.</summary>
[MemoryPackable]
public sealed partial record NPlusOneResponse
{
    public required int WindowMinutes { get; init; }

    public required int MinRepeats { get; init; }

    /// <summary>Worst first, by <see cref="NPlusOneOffender.TotalDurationMs"/>.</summary>
    public required IReadOnlyList<NPlusOneOffender> Offenders { get; init; }
}
