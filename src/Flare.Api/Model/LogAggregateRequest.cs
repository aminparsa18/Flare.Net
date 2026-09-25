using MemoryPack;

namespace Flare.Api.Model;

/// <summary>Optional secondary grouping dimension for <c>POST /api/logs/aggregate</c>.</summary>
public enum LogAggregateGroupBy
{
    None,
    Service,
    Level,

    /// <summary>
    /// One attribute key's value - <see cref="LogAggregateRequest.GroupByAttributeBag"/> +
    /// <see cref="LogAggregateRequest.GroupByAttributeKey"/> name which. Appended last so the
    /// existing members keep their MemoryPack ordinals.
    /// </summary>
    Attribute,

    /// <summary>OTel instrumentation scope name (<c>ScopeName</c> - the .NET logger category). Appended after <see cref="Attribute"/> for the same ordinal-stability reason.</summary>
    Scope,
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

    /// <summary>
    /// Optional chain of app-side transforms applied, in list order, to each returned
    /// bucket's <see cref="LogAggregateBucket.Count"/> after the ClickHouse query runs -
    /// see <see cref="Query.LogPostProcessor"/>. Null/empty = no post-processing (default).
    /// The Logs-explorer counterpart to <c>MetricQueryRequest.PostProcessFunctions</c>
    /// (ADR-0038/0039) - appended last, same append-only field-versioning convention that
    /// pair's own remarks document. When <see cref="GroupBy"/> is set, each distinct
    /// <see cref="LogAggregateBucket.GroupKey"/>'s buckets are processed as their own
    /// independent series, not run together as one - see <see cref="Query.LogPostProcessor"/>'s
    /// remarks.
    /// </summary>
    public IReadOnlyList<LogPostProcessFunction>? PostProcessFunctions { get; init; }

    /// <summary>
    /// Which attribute bag <see cref="GroupByAttributeKey"/> is looked up in when
    /// <see cref="GroupBy"/> is <see cref="LogAggregateGroupBy.Attribute"/>; ignored otherwise.
    /// Appended after <see cref="PostProcessFunctions"/> - same append-only versioning.
    /// </summary>
    public AttributeBag GroupByAttributeBag { get; init; } = AttributeBag.Log;

    /// <summary>
    /// Attribute key to group by when <see cref="GroupBy"/> is
    /// <see cref="LogAggregateGroupBy.Attribute"/> - required (non-blank) in that case,
    /// ignored otherwise. Only the <see cref="Query.LogAggregateQueryBuilder.AttributeGroupLimit"/>
    /// most frequent values in the window get their own series; every other value's events
    /// come back under a null <see cref="LogAggregateBucket.GroupKey"/> ("other"). Events
    /// missing the key group under an empty-string key, so the stacked total still matches
    /// the ungrouped chart.
    /// </summary>
    public string? GroupByAttributeKey { get; init; }
}

/// <summary>
/// Which per-query post-processing transform a <see cref="LogPostProcessFunction"/> applies -
/// the Logs-explorer counterpart to <c>MetricPostProcessFunctionType</c> (ADR-0038/0039),
/// shipped for Logs by ADR-0041. Same member set and ordinals - see
/// <see cref="Query.LogPostProcessor"/>'s remarks for how the semantics diverge from the
/// metrics version now that there's no nullable <see cref="LogAggregateBucket.Count"/> slot
/// to carry a "gap" through. MemoryPack encodes this as its numeric ordinal, so existing
/// members must keep their ordinals if new ones are ever appended.
/// </summary>
public enum LogPostProcessFunctionType
{
    ClampMin,
    ClampMax,
    Absolute,
    Log2,
    Log10,
    CumulativeSum,
    EwmaSmoothing,
    MedianSmoothing,
}

/// <summary>
/// One step in a <see cref="LogAggregateRequest.PostProcessFunctions"/> chain, applied in
/// list order (each function's output feeds the next) by <see cref="Query.LogPostProcessor"/>.
/// Field-for-field identical shape to <c>MetricPostProcessFunction</c>.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record LogPostProcessFunction
{
    public required LogPostProcessFunctionType Type { get; init; }

    /// <summary>Threshold for <see cref="LogPostProcessFunctionType.ClampMin"/>/<see cref="LogPostProcessFunctionType.ClampMax"/> - required for those two, ignored (may be null) for every other function.</summary>
    public double? Value { get; init; }

    /// <summary>
    /// Trailing lookback, in buckets, for <see cref="LogPostProcessFunctionType.EwmaSmoothing"/>
    /// (converted to a decay factor via the standard <c>alpha = 2 / (N + 1)</c> N-period
    /// formula) / <see cref="LogPostProcessFunctionType.MedianSmoothing"/> (a literal
    /// trailing window of up to N points) - required (&gt;= 1) for those two, ignored (may
    /// be null) for every other function.
    /// </summary>
    public int? WindowSize { get; init; }
}

/// <summary>
/// One bucketed value. <see cref="GroupKey"/> is null when <see cref="LogAggregateGroupBy.None"/>
/// was requested, or - for <see cref="LogAggregateGroupBy.Attribute"/> - for the rolled-up
/// "other" series (see <see cref="LogAggregateRequest.GroupByAttributeKey"/>). <see cref="Count"/> is <c>double</c> (not <c>long</c>) so the same shape
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
