using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/traces/db-statements</c> - the per-statement database view:
/// normalized statements ranked by total time, call rate or p95 across a window. See
/// <see cref="Query.DbStatementQueryBuilder"/>.
/// </summary>
[MemoryPackable]
public sealed partial record DbStatementRequest
{
    /// <summary>Lookback window; null/non-positive = <see cref="Query.DbStatementQueryBuilder.DefaultWindowMinutes"/>, clamped server-side.</summary>
    public int? WindowMinutes { get; init; }

    /// <summary>Where the window ends, as Unix epoch milliseconds; null = now.</summary>
    public long? EndUnixMs { get; init; }

    /// <summary>Exact <c>ServiceName</c> of the calling span. Null/empty = all services.</summary>
    public string? Service { get; init; }

    /// <summary>Exact database system (<c>postgresql</c>, <c>redis</c>, ...). Null/empty = all.</summary>
    public string? DbSystem { get; init; }

    /// <summary>One of <see cref="Query.DbStatementQueryBuilder.SortKeys"/>; null/unknown = <c>totalTime</c>.</summary>
    public string? SortBy { get; init; }
}

/// <summary>One normalized statement for one service and database system, aggregated over the window.</summary>
[MemoryPackable]
public sealed partial record DbStatementRow
{
    public required string ServiceName { get; init; }

    public required string DbSystem { get; init; }

    /// <summary>The normalized statement (literals replaced by <c>?</c>), or <c>operation collection</c> when the span carried no query text.</summary>
    public required string Statement { get; init; }

    /// <summary>Executions in the window (sample-weighted).</summary>
    public required ulong CallCount { get; init; }

    public required ulong ErrorCount { get; init; }

    /// <summary>Executions per minute over the whole window.</summary>
    public required double CallsPerMinute { get; init; }

    public required double AvgDurationMs { get; init; }

    public required double P95DurationMs { get; init; }

    /// <summary>Summed duration of every execution, milliseconds - what "slowest overall" ranks by.</summary>
    public required double TotalDurationMs { get; init; }

    /// <summary>The slowest single execution's trace, for the drill-down link.</summary>
    public required string ExampleTraceId { get; init; }
}

/// <summary>Response body for <c>POST /api/traces/db-statements</c>.</summary>
[MemoryPackable]
public sealed partial record DbStatementResponse
{
    public required int WindowMinutes { get; init; }

    public required string SortBy { get; init; }

    public required IReadOnlyList<DbStatementRow> Statements { get; init; }
}
