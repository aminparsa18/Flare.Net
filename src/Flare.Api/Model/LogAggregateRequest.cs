namespace Flare.Api.Model;

/// <summary>Optional secondary grouping dimension for <c>POST /api/logs/aggregate</c>.</summary>
public enum LogAggregateGroupBy
{
    None,
    Service,
    Level,
}

/// <summary>Request body for <c>POST /api/logs/aggregate</c> - volume-over-time chart data.</summary>
public sealed record LogAggregateRequest
{
    /// <summary>See <see cref="LogSearchRequest.Filter"/>'s doc comment - the same JSON-deserialization caveat applies here.</summary>
    public LogFilter Filter { get; init; } = new();

    /// <summary>Bucket width, e.g. 60 for 1-minute buckets. Compiles to <c>toStartOfInterval(Timestamp, INTERVAL n SECOND)</c>.</summary>
    public required int BucketWidthSeconds { get; init; }

    public LogAggregateGroupBy GroupBy { get; init; } = LogAggregateGroupBy.None;
}

/// <summary>One bucketed count. <see cref="GroupKey"/> is null when <see cref="LogAggregateGroupBy.None"/> was requested.</summary>
public sealed record LogAggregateBucket
{
    public required DateTimeOffset BucketStart { get; init; }

    public string? GroupKey { get; init; }

    public required long Count { get; init; }
}

/// <summary>Response body for <c>POST /api/logs/aggregate</c>.</summary>
public sealed record LogAggregateResponse
{
    public required IReadOnlyList<LogAggregateBucket> Buckets { get; init; }
}
