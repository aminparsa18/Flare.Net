namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/spans/duration-percentile</c> - where one span's duration
/// ranks among spans with the same service + name in a window around it (see
/// <c>Query.SpanDurationPercentileQueryBuilder</c>). JSON-only, like the other small
/// span-detail lookups: no MemoryPack/TypeScript-generated twin.
/// </summary>
public sealed record SpanDurationPercentileRequest
{
    public string ServiceName { get; init; } = "";

    public string Name { get; init; } = "";

    public ulong DurationNano { get; init; }

    /// <summary>The span's own start; the comparison window is centred on it.</summary>
    public DateTimeOffset StartTime { get; init; }
}

/// <summary>Response body for <c>POST /api/spans/duration-percentile</c>.</summary>
public sealed record SpanDurationPercentileResponse
{
    /// <summary>Spans compared against (the span itself included); 0 if none matched.</summary>
    public required long SampleCount { get; init; }

    /// <summary>Share of compared spans no longer than this one, 0-100.</summary>
    public required double Percentile { get; init; }

    public required double P50Nano { get; init; }

    public required double P95Nano { get; init; }

    public required double P99Nano { get; init; }
}
