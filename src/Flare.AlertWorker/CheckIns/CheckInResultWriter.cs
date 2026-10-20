using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using Flare.Api.Query;
using Microsoft.Extensions.Options;

namespace Flare.AlertWorker.CheckIns;

/// <summary>
/// Writes each monitor's <see cref="CheckInMetrics.Up"/> as a gauge point in <c>metrics_gauge</c> - the same table
/// <c>Flare.Ingest</c> fills, so a metric alert reads a check-in like any other metric. Direct insert, for the reason
/// <see cref="Synthetic.SyntheticResultWriter"/> gives: the worker is inside Flare, and a detour through Ingest and
/// Redis would go quiet exactly when they are down.
/// </summary>
public sealed class CheckInResultWriter(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits)
{
    public async Task WriteAsync(IReadOnlyList<(string Slug, bool Up)> results, DateTimeOffset time, CancellationToken cancellationToken)
    {
        if (results.Count == 0)
        {
            return;
        }

        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("slugs", results.Select(r => r.Slug).ToArray());
        parameters.AddParameter("values", results.Select(r => r.Up ? 1d : 0d).ToArray());
        parameters.AddParameter("name", CheckInMetrics.Up);
        parameters.AddParameter("service", CheckInMetrics.ServiceName);
        parameters.AddParameter("time", time.UtcDateTime);

        // One INSERT ... SELECT for every monitor: arrayZip + arrayJoin turns the two parallel arrays into rows,
        // and map() builds the attribute set without needing the driver to bind a Map parameter.
        const string sql = """
            INSERT INTO metrics_gauge
                (MetricName, Description, Unit, ServiceName, ResourceSchemaUrl, ResourceAttributes, ScopeSchemaUrl, ScopeName,
                 ScopeVersion, ScopeAttributes, DataPointAttributes, StartTime, Time, Value, IngestedAt)
            SELECT
                {name:String}, '', '1', {service:String}, '', map(), '', 'flare.checkin',
                '', map(),
                map('monitor', p.1),
                toDateTime64({time:DateTime64(3)}, 9), toDateTime64({time:DateTime64(3)}, 9), p.2, now64(9)
            FROM (SELECT arrayJoin(arrayZip({slugs:Array(String)}, {values:Array(Float64)})) AS p)
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
    }
}
