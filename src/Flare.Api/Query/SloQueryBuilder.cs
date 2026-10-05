using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using Flare.Api.Slos;

namespace Flare.Api.Query;

/// <summary>A parameterized SLO query plus its bound parameters.</summary>
public sealed record SloSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure SQL builder over <c>span_sli_minute</c> (migration 0049) for one <see cref="Slo"/>:
/// good/bad counts over several trailing windows in a single scan, and an hourly series for
/// the burn-down chart. See <c>docs-internal/adr/0108-slo-error-budgets.md</c>.
/// </summary>
/// <remarks>
/// The bad-event expression is the only thing that differs by <see cref="SloKind"/>:
/// <c>ErrorCount</c> for availability, <c>TotalCount - Under{T}ms</c> for latency (the ladder
/// column of the SLO's threshold). Window starts are floored to the minute so a "5m" window
/// includes the partial minute it began in rather than dropping it.
/// </remarks>
public static class SloQueryBuilder
{
    /// <summary>The trailing windows the status endpoint reports a burn rate for (5m, 30m, 1h, 6h, 24h).</summary>
    public static readonly IReadOnlyList<int> StatusWindowSeconds = [300, 1800, 3600, 21600, 86400];

    /// <summary>The SQL expression (over <c>span_sli_minute</c> columns) counting a row's bad events.</summary>
    public static string BadExpression(Slo slo) => slo.Kind switch
    {
        SloKind.Latency => $"TotalCount - {SloLatencyLadder.Column(slo.LatencyThresholdMs) ?? throw new InvalidOperationException($"{slo.LatencyThresholdMs}ms is not a latency ladder rung.")}",
        _ => "ErrorCount",
    };

    /// <summary>
    /// One row: <c>t0, b0, t1, b1, ...</c> - total and bad events for each entry of
    /// <paramref name="windowSeconds"/>, in order.
    /// </summary>
    public static SloSql BuildCounts(Slo slo, IReadOnlyList<int> windowSeconds, DateTimeOffset now)
    {
        var parameters = ScopeParameters(slo);
        parameters.AddParameter("to", CeilToMinute(now.UtcDateTime));

        var bad = BadExpression(slo);
        var columns = new List<string>(windowSeconds.Count * 2);
        for (var i = 0; i < windowSeconds.Count; i++)
        {
            parameters.AddParameter($"f{i}", FloorToMinute(now.UtcDateTime - TimeSpan.FromSeconds(windowSeconds[i])));
            columns.Add($"sumIf(TotalCount, TimeBucket >= {{f{i}:DateTime}}) AS t{i}");
            columns.Add($"sumIf({bad}, TimeBucket >= {{f{i}:DateTime}}) AS b{i}");
        }

        var widest = windowSeconds.Max();
        parameters.AddParameter("from", FloorToMinute(now.UtcDateTime - TimeSpan.FromSeconds(widest)));

        var sql = $"SELECT\n    {string.Join(",\n    ", columns)}\n" +
            "FROM span_sli_minute\n" +
            $"WHERE {ScopeWhere(slo, parameters)}\n" +
            "    AND TimeBucket >= {from:DateTime} AND TimeBucket < {to:DateTime}";
        return new SloSql(sql, parameters);
    }

    /// <summary>Rows of <c>Hour, Total, Bad</c> across the SLO's window, oldest first.</summary>
    public static SloSql BuildSeries(Slo slo, DateTimeOffset now)
    {
        var parameters = ScopeParameters(slo);
        parameters.AddParameter("from", FloorToMinute(now.UtcDateTime - TimeSpan.FromDays(slo.WindowDays)));
        parameters.AddParameter("to", CeilToMinute(now.UtcDateTime));

        var sql = "SELECT\n" +
            "    toStartOfHour(TimeBucket) AS Hour,\n" +
            "    sum(TotalCount) AS Total,\n" +
            $"    sum({BadExpression(slo)}) AS Bad\n" +
            "FROM span_sli_minute\n" +
            $"WHERE {ScopeWhere(slo, parameters)}\n" +
            "    AND TimeBucket >= {from:DateTime} AND TimeBucket < {to:DateTime}\n" +
            "GROUP BY Hour\n" +
            "ORDER BY Hour";
        return new SloSql(sql, parameters);
    }

    private static string ScopeWhere(Slo slo, ClickHouseParameterCollection parameters) =>
        "ServiceName = {service:String}" + (slo.OperationName.Length > 0 ? " AND Name = {operation:String}" : "") + ServiceScope.Suffix(parameters);

    private static ClickHouseParameterCollection ScopeParameters(Slo slo)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("service", slo.ServiceName);
        if (slo.OperationName.Length > 0)
        {
            parameters.AddParameter("operation", slo.OperationName);
        }

        return parameters;
    }

    private static DateTime FloorToMinute(DateTime value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMinute));

    private static DateTime CeilToMinute(DateTime value)
    {
        var floored = FloorToMinute(value);
        return floored == value ? floored : floored.AddMinutes(1);
    }
}
