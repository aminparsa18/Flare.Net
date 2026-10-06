using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Api.Synthetic;
using Microsoft.Extensions.Options;

namespace Flare.AlertWorker.Synthetic;

/// <summary>
/// Writes a <see cref="SyntheticProbeResult"/> as gauge points in <c>metrics_gauge</c> - the same table
/// <c>Flare.Ingest</c> fills, so metric alerts, charts and dashboards read probes like any other metric.
/// Direct insert, not through OTLP: the probe runs inside Flare, and a self-monitoring detour through
/// Ingest and Redis would stop reporting exactly when they are down.
/// </summary>
public sealed class SyntheticResultWriter(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits)
{
    /// <summary>The (metric name, unit, value) points for <paramref name="result"/>. Pure.</summary>
    public static IReadOnlyList<(string Name, string Unit, double Value)> Points(SyntheticProbeResult result)
    {
        var points = new List<(string, string, double)>
        {
            (SyntheticMetrics.Up, "1", result.Up ? 1 : 0),
            (SyntheticMetrics.Duration, "ms", result.DurationMs),
        };
        if (result.HttpStatus is { } status)
        {
            points.Add((SyntheticMetrics.HttpStatusCode, "1", status));
        }

        if (result.CertExpiryDays is { } days)
        {
            points.Add((SyntheticMetrics.CertExpiryDays, "d", days));
        }

        return points;
    }

    public async Task WriteAsync(SyntheticMonitor monitor, SyntheticProbeResult result, DateTimeOffset time, CancellationToken cancellationToken)
    {
        var points = Points(result);
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("names", points.Select(p => p.Name).ToArray());
        parameters.AddParameter("units", points.Select(p => p.Unit).ToArray());
        parameters.AddParameter("values", points.Select(p => p.Value).ToArray());
        parameters.AddParameter("service", SyntheticMetrics.ServiceName);
        parameters.AddParameter("monitor", monitor.Name);
        parameters.AddParameter("kind", monitor.Kind.ToString());
        parameters.AddParameter("target", monitor.Target);
        parameters.AddParameter("time", time.UtcDateTime);

        // One INSERT ... SELECT for all points: arrayZip + arrayJoin turns the three parallel arrays into rows,
        // and map() builds the attribute set without needing the driver to bind a Map parameter.
        const string sql = """
            INSERT INTO metrics_gauge
                (MetricName, Description, Unit, ServiceName, ResourceSchemaUrl, ResourceAttributes, ScopeSchemaUrl, ScopeName,
                 ScopeVersion, ScopeAttributes, DataPointAttributes, StartTime, Time, Value, IngestedAt)
            SELECT
                p.1, '', p.2, {service:String}, '', map(), '', 'flare.synthetic',
                '', map(),
                map('monitor', {monitor:String}, 'kind', {kind:String}, 'target', {target:String}),
                toDateTime64({time:DateTime64(3)}, 9), toDateTime64({time:DateTime64(3)}, 9), p.3, now64(9)
            FROM (SELECT arrayJoin(arrayZip({names:Array(String)}, {units:Array(String)}, {values:Array(Float64)})) AS p)
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
    }
}
