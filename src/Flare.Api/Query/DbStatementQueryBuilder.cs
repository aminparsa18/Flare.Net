using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT</c> for the per-statement database view.</summary>
public sealed record DbStatementSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure SQL builder for the per-statement database view (<c>POST /api/traces/db-statements</c>):
/// database client spans grouped by service, database system and normalized statement, with
/// call count, error count, average, p95 and total time. Computed from <c>spans</c> at query
/// time - no new table (ADR-0271).
/// </summary>
/// <remarks>
/// Statement normalization is <see cref="NPlusOneQueryBuilder.StatementExpr"/>, so a statement
/// here is the same string the N+1 list shows. Unlike N+1 this is not scoped to one trace:
/// every database span counts, parented or not, because a root database span is still a real
/// execution. Counts and percentiles are weighted by <c>SampleWeight</c> so ingest-time trace
/// sampling (ADR-0122) doesn't understate rate.
/// </remarks>
public static class DbStatementQueryBuilder
{
    public const int DefaultWindowMinutes = 60;
    public const int MinWindowMinutes = 5;
    public const int MaxWindowMinutes = 1440;

    public const int MaxRows = 100;

    public const string DefaultSort = "totalTime";

    /// <summary>Accepted <c>SortBy</c> values mapped to the SQL alias they order by (a whitelist - never interpolate the request value).</summary>
    public static readonly IReadOnlyDictionary<string, string> SortKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["totalTime"] = "TotalNano",
        ["p95"] = "P95Nano",
        ["calls"] = "CallCount",
        ["errors"] = "ErrorCount",
    };

    public static int ClampWindowMinutes(int? requested) =>
        requested is > 0 ? Math.Clamp(requested.Value, MinWindowMinutes, MaxWindowMinutes) : DefaultWindowMinutes;

    public static string ResolveSort(string? requested) =>
        requested is not null && SortKeys.ContainsKey(requested) ? requested : DefaultSort;

    public static DbStatementSql Build(DbStatementRequest request, int windowMinutes, string sortBy, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("from", end.AddMinutes(-windowMinutes).UtcDateTime);
        parameters.AddParameter("to", end.UtcDateTime);
        parameters.AddParameter("errorStatus", "STATUS_CODE_ERROR");
        parameters.AddParameter("limit", (uint)MaxRows);

        var clauses = new List<string>
        {
            "StartTime >= {from:DateTime64(9)}",
            "StartTime < {to:DateTime64(9)}",
            $"{ServiceCallBreakdownQueryBuilder.DbSystemExpr} != ''",
        };

        if (!string.IsNullOrWhiteSpace(request.Service))
        {
            parameters.AddParameter("service", request.Service);
            clauses.Add("ServiceName = {service:String}");
        }

        if (!string.IsNullOrWhiteSpace(request.DbSystem))
        {
            parameters.AddParameter("dbSystem", request.DbSystem);
            clauses.Add($"{ServiceCallBreakdownQueryBuilder.DbSystemExpr} = {{dbSystem:String}}");
        }

        ServiceScope.Append(clauses, parameters);

        var sql =
            "SELECT\n" +
            "    ServiceName,\n" +
            $"    {ServiceCallBreakdownQueryBuilder.DbSystemExpr} AS DbSystem,\n" +
            $"    {NPlusOneQueryBuilder.StatementExpr} AS Statement,\n" +
            "    sum(SampleWeight) AS CallCount,\n" +
            "    sumIf(SampleWeight, StatusCode = {errorStatus:String}) AS ErrorCount,\n" +
            "    toFloat64(sum(DurationNano * SampleWeight)) AS TotalNano,\n" +
            "    quantileTDigestWeighted(0.95)(DurationNano, SampleWeight) AS P95Nano,\n" +
            "    argMax(TraceId, DurationNano) AS ExampleTraceId\n" +
            "FROM spans\n" +
            "WHERE " + string.Join(" AND ", clauses) + "\n" +
            "GROUP BY ServiceName, DbSystem, Statement\n" +
            "HAVING Statement != ''\n" +
            $"ORDER BY {SortKeys[sortBy]} DESC, ServiceName, Statement\n" +
            "LIMIT {limit:UInt32}";

        return new DbStatementSql(sql, parameters);
    }
}
