using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>A parameterized per-service Apdex-bucket-count query plus its bound parameters.</summary>
public sealed record ServiceApdexSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure window/threshold-overrides → parameterized SQL builder for the Traces page's
/// Services tab Apdex score - see docs-internal/adr/0032-apdex-score-per-service.md for
/// why this is always its own live query rather than folded into
/// <see cref="ServiceMetricsQueryBuilder"/>'s pre-aggregation. Same "pure function, no
/// ClickHouse dependency" style as <see cref="ServiceOverviewQueryBuilder"/>, which this
/// mirrors closely (same window/root-span/resource-attribute predicates) but for a
/// different aggregate shape.
/// </summary>
/// <remarks>
/// Classifies each root span into the standard Apdex buckets against a *per-service*
/// threshold T: satisfied (duration &lt;= T), tolerating (T &lt; duration &lt;= 4T),
/// frustrated (duration &gt; 4T, the implicit remainder - not counted here since the
/// score only needs satisfied/tolerating counts plus the total <see cref="ServiceOverviewQueryBuilder"/>'s
/// query already produces as <c>RequestCount</c> for the same predicate).
/// <para>
/// T varies per <c>ServiceName</c>, so it can't be a single bound parameter - it's
/// expressed as a ClickHouse <c>multiIf(ServiceName = svc0, t0, ServiceName = svc1, t1,
/// ..., default)</c> built from <paramref name="thresholdOverridesMs"/>-turned-nanoseconds,
/// each service name/threshold pair still bound as a real parameter (never
/// string-interpolated) to stay injection-safe.
/// </para>
/// </remarks>
public static class ServiceApdexQueryBuilder
{
    /// <summary>Classic APM default Apdex threshold, milliseconds - applies to any
    /// service with no row in <c>ApdexThresholds</c>.</summary>
    public const int DefaultThresholdMs = 500;

    private const ulong NanosPerMilli = 1_000_000UL;

    public static ServiceApdexSql Build(
        TimeSpan window,
        DateTimeOffset now,
        IReadOnlyDictionary<string, int> thresholdOverridesMs,
        IReadOnlyList<ResourceAttributeFilter>? resourceAttributes = null)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("from", (now - window).UtcDateTime);
        parameters.AddParameter("to", now.UtcDateTime);
        parameters.AddParameter("apdexDefaultThresholdNano", DefaultThresholdMs * NanosPerMilli);

        var clauses = new List<string>
        {
            "StartTime >= {from:DateTime64(9)} AND StartTime < {to:DateTime64(9)}",
            "ParentSpanId = ''",
        };
        ResourceAttributeFilterSqlBuilder.AppendClauses(clauses, parameters, resourceAttributes, columnAlias: string.Empty, paramPrefix: string.Empty);

        var thresholdExpr = BuildThresholdExpr(thresholdOverridesMs, parameters);

        var sql = "SELECT\n" +
            "    ServiceName,\n" +
            $"    countIf(DurationNano <= {thresholdExpr}) AS ApdexSatisfiedCount,\n" +
            $"    countIf(DurationNano > {thresholdExpr} AND DurationNano <= {thresholdExpr} * 4) AS ApdexToleratingCount\n" +
            "FROM spans\n" +
            "WHERE " + string.Join(" AND ", clauses) + "\n" +
            "GROUP BY ServiceName";

        return new ServiceApdexSql(sql, parameters);
    }

    /// <summary>Builds the per-service threshold SQL expression (nanoseconds) and binds
    /// each override's service name/threshold as a real parameter. No overrides at all
    /// collapses to the plain default-threshold parameter, skipping <c>multiIf</c>
    /// entirely.</summary>
    private static string BuildThresholdExpr(IReadOnlyDictionary<string, int> overrides, ClickHouseParameterCollection parameters)
    {
        if (overrides.Count == 0)
        {
            return "{apdexDefaultThresholdNano:UInt64}";
        }

        var branches = new List<string>(overrides.Count);
        var i = 0;
        foreach (var (serviceName, thresholdMs) in overrides)
        {
            var svcParam = $"apdexSvc{i}";
            var thresholdParam = $"apdexThreshold{i}";
            parameters.AddParameter(svcParam, serviceName);
            parameters.AddParameter(thresholdParam, (ulong)thresholdMs * NanosPerMilli);
            branches.Add($"ServiceName = {{{svcParam}:String}}, {{{thresholdParam}:UInt64}}");
            i++;
        }

        return $"multiIf({string.Join(", ", branches)}, {{apdexDefaultThresholdNano:UInt64}})";
    }
}
