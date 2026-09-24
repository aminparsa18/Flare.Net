using ClickHouse.Driver;

namespace Flare.Api.Query;

/// <summary>
/// Builds the <see cref="QueryOptions"/> every query service passes to ClickHouse from
/// the configured <see cref="QueryLimitsOptions"/> - the one place these caps are spelled
/// out, instead of each service carrying its own hard-coded copy. Returns a fresh
/// <see cref="QueryOptions"/> per call since callers (e.g. <see cref="SpanQueryService"/>'s
/// trace-by-id lookup) add settings to it.
/// </summary>
public static class QuerySafety
{
    /// <summary>Execution-time, scan and result-size caps - the default for any query over user data.</summary>
    public static QueryOptions Full(QueryLimitsOptions limits) => new()
    {
        CustomSettings = new Dictionary<string, object>
        {
            ["max_execution_time"] = limits.MaxExecutionSeconds,
            ["timeout_before_checking_execution_speed"] = 0,
            ["max_rows_to_read"] = limits.MaxRowsToRead,
            ["max_result_rows"] = limits.MaxResultRows,
            ["result_overflow_mode"] = "break",
        },
    };

    /// <summary>Execution-time cap only - for <c>system.*</c> introspection reads, which aren't user-filtered data.</summary>
    public static QueryOptions ExecutionTimeOnly(QueryLimitsOptions limits) => new()
    {
        CustomSettings = new Dictionary<string, object>
        {
            ["max_execution_time"] = limits.MaxExecutionSeconds,
            ["timeout_before_checking_execution_speed"] = 0,
        },
    };

    /// <summary>
    /// Alert-evaluation cap: <see cref="QueryLimitsOptions.AlertEvaluationMaxExecutionSeconds"/>
    /// plus the scan cap, but no result-row cap (these are single-value counts/lookups).
    /// </summary>
    public static QueryOptions AlertEvaluation(QueryLimitsOptions limits) => new()
    {
        CustomSettings = new Dictionary<string, object>
        {
            ["max_execution_time"] = limits.AlertEvaluationMaxExecutionSeconds,
            ["timeout_before_checking_execution_speed"] = 0,
            ["max_rows_to_read"] = limits.MaxRowsToRead,
        },
    };
}
