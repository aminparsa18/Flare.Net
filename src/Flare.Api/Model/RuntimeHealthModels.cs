using MemoryPack;

namespace Flare.Api.Model;

/// <summary>What a <see cref="RuntimeHealthFinding"/> detected. See <see cref="Query.RuntimeHealthDetector"/> for each rule.</summary>
public enum RuntimeFindingKind
{
    /// <summary>Thread-pool queue backing up while completed work items flatline.</summary>
    ThreadPoolStarvation,

    /// <summary>Sustained high share of wall-clock time paused in garbage collection.</summary>
    GcPressure,

    /// <summary>Lock contention rate far above its baseline.</summary>
    LockContention,

    /// <summary>Exception rate far above its baseline.</summary>
    ExceptionRate,
}

public enum RuntimeFindingSeverity
{
    Warning,

    /// <summary>Still happening at the end of the window and past the kind's critical level.</summary>
    Critical,
}

/// <summary>
/// Request body for <c>POST /api/services/runtime-health</c>: the .NET runtime health
/// findings for one service over a window. See <see cref="Query.RuntimeHealthQueryBuilder"/>.
/// </summary>
[MemoryPackable]
public sealed partial record RuntimeHealthRequest
{
    /// <summary>Exact <c>ServiceName</c>. Required.</summary>
    public string? Service { get; init; }

    /// <summary>Lookback window; null/non-positive = <see cref="Query.RuntimeHealthQueryBuilder.DefaultWindowMinutes"/>, clamped server-side.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Where the window ends, as Unix epoch milliseconds; null = now.</summary>
    public long? EndUnixMs { get; init; }
}

/// <summary>One detected problem on one instance of the service.</summary>
[MemoryPackable]
public sealed partial record RuntimeHealthFinding
{
    public required RuntimeFindingKind Kind { get; init; }

    public required RuntimeFindingSeverity Severity { get; init; }

    /// <summary><c>service.instance.id</c>, else pod name, else <c>host.name</c>; empty when the service reports none.</summary>
    public required string Instance { get; init; }

    /// <summary>Start of the first affected bucket, Unix epoch milliseconds.</summary>
    public required long StartUnixMs { get; init; }

    /// <summary>End of the last affected bucket (capped at the window end), Unix epoch milliseconds.</summary>
    public required long EndUnixMs { get; init; }

    /// <summary>The run reaches the end of the window, so it may still be happening.</summary>
    public required bool Ongoing { get; init; }

    /// <summary>
    /// Headline figure at its worst: peak queue length (<see cref="RuntimeFindingKind.ThreadPoolStarvation"/>),
    /// peak fraction of time in GC, 0-1 (<see cref="RuntimeFindingKind.GcPressure"/>), or peak events per
    /// second (<see cref="RuntimeFindingKind.LockContention"/>, <see cref="RuntimeFindingKind.ExceptionRate"/>).
    /// </summary>
    public required double Value { get; init; }

    /// <summary>
    /// What normal looks like on this instance: median completed work items per second
    /// (starvation) or the typical events per second (the two rate kinds). Null for GC pressure.
    /// </summary>
    public double? Baseline { get; init; }
}

/// <summary>Response body for <c>POST /api/services/runtime-health</c>.</summary>
[MemoryPackable]
public sealed partial record RuntimeHealthResponse
{
    public required int WindowMinutes { get; init; }

    /// <summary>The service sent at least one of the <c>dotnet.*</c> runtime metrics in the window. False means "no data", not "healthy".</summary>
    public required bool HasRuntimeMetrics { get; init; }

    /// <summary>Critical first, then most recent first.</summary>
    public required IReadOnlyList<RuntimeHealthFinding> Findings { get; init; }
}
