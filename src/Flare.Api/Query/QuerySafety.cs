using ClickHouse.Driver;

namespace Flare.Api.Query;

/// <summary>
/// Builds the <see cref="QueryOptions"/> every query service passes to ClickHouse from
/// the configured <see cref="QueryLimitsOptions"/> - the one place these caps are spelled
/// out, instead of each service carrying its own hard-coded copy. Returns a fresh
/// <see cref="QueryOptions"/> per call since callers (e.g. <see cref="SpanQueryService"/>'s
/// trace-by-id lookup) add settings to it. Each one also carries the request's
/// <see cref="ClickHouseQuotaKey"/>, so the <c>flare_query</c> quota charges the caller (ADR-0286), and, with
/// <see cref="QueryLimitsOptions.RowPolicyScope"/>, the request's <see cref="ServiceScope"/> for the row
/// policies on <c>flare_query</c> (ADR-0287).
/// </summary>
public static class QuerySafety
{
    /// <summary>Execution-time, scan and result-size caps - the default for any query over user data.</summary>
    public static QueryOptions Full(QueryLimitsOptions limits) => new()
    {
        CustomHeaders = ClickHouseQuotaKey.Headers(),
        CustomSettings = new Dictionary<string, object>(Scope(limits))
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
        CustomHeaders = ClickHouseQuotaKey.Headers(),
        CustomSettings = new Dictionary<string, object>(Scope(limits))
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
        CustomHeaders = ClickHouseQuotaKey.Headers(),
        CustomSettings = new Dictionary<string, object>(Scope(limits))
        {
            ["max_execution_time"] = limits.AlertEvaluationMaxExecutionSeconds,
            ["timeout_before_checking_execution_speed"] = 0,
            ["max_rows_to_read"] = limits.MaxRowsToRead,
        },
    };

    /// <summary>The row-policy scope setting, or nothing when <see cref="QueryLimitsOptions.RowPolicyScope"/> is off.</summary>
    private static Dictionary<string, object> Scope(QueryLimitsOptions limits) =>
        limits.RowPolicyScope
            ? new() { [ServiceScope.RowPolicySettingName] = ServiceScope.RowPolicySettingValue() }
            : [];
}
