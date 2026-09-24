namespace Flare.Api.Query;

/// <summary>
/// ClickHouse per-query execution caps shared by every query service, bound from the
/// <c>Query</c> configuration section (e.g. <c>Query__MaxExecutionSeconds=120</c>). The
/// defaults are the values each service used to hard-code - self-hosted ClickHouse has
/// no caps of its own, so these are what stop a runaway query (see the
/// <c>clickhouse-best-practices</c> skill's <c>agent-query-safety</c> rule). Larger
/// installs raise them for heavy searches; <c>0</c> means "unlimited", which is
/// ClickHouse's own meaning for each of these settings, not something Flare adds.
/// </summary>
public sealed class QueryLimitsOptions
{
    public const string SectionName = "Query";

    /// <summary>ClickHouse <c>max_execution_time</c> for every user-facing/CRUD query.</summary>
    public int MaxExecutionSeconds { get; set; } = 30;

    /// <summary>ClickHouse <c>max_rows_to_read</c>.</summary>
    public long MaxRowsToRead { get; set; } = 1_000_000_000;

    /// <summary>ClickHouse <c>max_result_rows</c>, applied with <c>result_overflow_mode = 'break'</c> (truncates rather than errors).</summary>
    public long MaxResultRows { get; set; } = 10_000;

    /// <summary>
    /// Tighter <c>max_execution_time</c> for the per-rule queries <c>AlertEvaluationWorker</c>
    /// runs on every poll tick - a runaway query there blocks the whole tick, not just one
    /// dashboard request.
    /// </summary>
    public int AlertEvaluationMaxExecutionSeconds { get; set; } = 10;
}
