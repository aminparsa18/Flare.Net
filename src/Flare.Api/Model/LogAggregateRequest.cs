using MemoryPack;

namespace Flare.Api.Model;

/// <summary>Optional secondary grouping dimension for <c>POST /api/logs/aggregate</c>.</summary>
public enum LogAggregateGroupBy
{
    None,
    Service,
    Level,
}

/// <summary>Request body for <c>POST /api/logs/aggregate</c> - volume-over-time chart data.</summary>
[MemoryPackable]
public sealed partial record LogAggregateRequest
{
    /// <summary>See <see cref="LogSearchRequest.Filter"/>'s doc comment - the same JSON-deserialization caveat applies here.</summary>
    public LogFilter Filter { get; init; } = new();

    /// <summary>Bucket width, e.g. 60 for 1-minute buckets. Compiles to <c>toStartOfInterval(Timestamp, INTERVAL n SECOND)</c>.</summary>
    public required int BucketWidthSeconds { get; init; }

    public LogAggregateGroupBy GroupBy { get; init; } = LogAggregateGroupBy.None;
}

/// <summary>
/// One bucketed value. <see cref="GroupKey"/> is null when <see cref="LogAggregateGroupBy.None"/>
/// was requested. <see cref="Count"/> is <c>double</c> (not <c>long</c>) so the same shape
/// can carry a SQL-query-row <c>avg()</c>/<c>sum()</c> result (see
/// <c>Query.LogQl.LogQlQueryBuilder</c>) as well as this endpoint's own always-integral
/// <c>count()</c> - a whole-number value still round-trips through JSON exactly (e.g. `25`,
/// not `25.0`), so this is a no-op for every existing <c>/api/logs/aggregate</c> caller.
/// </summary>
[MemoryPackable]
public sealed partial record LogAggregateBucket
{
    public required DateTimeOffset BucketStart { get; init; }

    public string? GroupKey { get; init; }

    public required double Count { get; init; }
}

/// <summary>Response body for <c>POST /api/logs/aggregate</c>.</summary>
[MemoryPackable]
public sealed partial record LogAggregateResponse
{
    public required IReadOnlyList<LogAggregateBucket> Buckets { get; init; }
}
