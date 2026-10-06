using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/profiles/types</c> - which (service, sample type) profile
/// series exist in the window. See docs-internal/adr/0141-continuous-profiling-ingest.md.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record ProfileTypesRequest
{
    /// <summary>Lookback window; null/non-positive = <see cref="Query.ProfileQueryBuilder.DefaultWindowMinutes"/>, clamped server-side.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Where the window ends, as Unix epoch milliseconds; null = now.</summary>
    public long? EndUnixMs { get; init; }
}

/// <summary>One (service, sample type) profile series and how much data it has in the window.</summary>
[MemoryPackable]
public sealed partial record ProfileTypeInfo
{
    public required string Service { get; init; }

    /// <summary>Profile sample type, e.g. <c>cpu</c> or <c>alloc_space</c>.</summary>
    public required string SampleType { get; init; }

    /// <summary>Unit of the series' values, e.g. <c>nanoseconds</c>, <c>bytes</c>.</summary>
    public required string SampleUnit { get; init; }

    public required ulong SampleCount { get; init; }

    public required long LastSeenUnixMs { get; init; }
}

[MemoryPackable]
public sealed partial record ProfileTypesResponse
{
    public required int WindowMinutes { get; init; }

    public required IReadOnlyList<ProfileTypeInfo> Types { get; init; }
}

/// <summary>
/// Request body for <c>POST /api/profiles/flamegraph</c>: the merged call tree of every sample
/// of one service and sample type in the window, optionally narrowed to the samples taken
/// during one span (or any span of one trace).
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record FlameGraphRequest
{
    /// <summary>Exact <c>ServiceName</c>. Required.</summary>
    public string? Service { get; init; }

    /// <summary>Exact sample type, e.g. <c>cpu</c>. Required - values of different types aren't summable.</summary>
    public string? SampleType { get; init; }

    public int? WindowMinutes { get; init; }

    public long? EndUnixMs { get; init; }

    /// <summary>Lower-hex trace id: only samples taken during a span of this trace.</summary>
    public string? TraceId { get; init; }

    /// <summary>Lower-hex span id: only samples taken during this span. Usually paired with <see cref="TraceId"/>.</summary>
    public string? SpanId { get; init; }
}

/// <summary>One frame of the merged call tree.</summary>
[MemoryPackable]
public sealed partial record FlameGraphNode
{
    public required string Name { get; init; }

    /// <summary>Samples' summed value for this frame and everything below it.</summary>
    public required long Total { get; init; }

    /// <summary>The part of <see cref="Total"/> spent in this frame itself, not its children.</summary>
    public required long Self { get; init; }

    public required IReadOnlyList<FlameGraphNode> Children { get; init; }
}

[MemoryPackable]
public sealed partial record FlameGraphResponse
{
    public required int WindowMinutes { get; init; }

    public required string SampleUnit { get; init; }

    /// <summary>Number of distinct stacks merged.</summary>
    public required int StackCount { get; init; }

    /// <summary>True when the stack count hit the cap, so the lightest stacks were left out.</summary>
    public required bool Truncated { get; init; }

    /// <summary>Synthetic <c>all</c> root whose <see cref="FlameGraphNode.Total"/> is the sum over every stack; its children are the real root frames.</summary>
    public required FlameGraphNode Root { get; init; }
}
