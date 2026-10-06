using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT</c> for a profiles query, ready for <see cref="ProfileQueryService"/>.</summary>
public sealed record ProfileSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure SQL builder over <c>profile_samples</c> (<c>db/clickhouse/0065_profile_samples.sql</c>).
/// A flame graph is one <c>GROUP BY Stack</c>; the tree itself is assembled by
/// <see cref="FlameGraphBuilder"/>. See docs-internal/adr/0141-continuous-profiling-ingest.md.
/// </summary>
public static class ProfileQueryBuilder
{
    public const int DefaultWindowMinutes = 60;
    public const int MinWindowMinutes = 1;
    public const int MaxWindowMinutes = 1440;

    /// <summary>
    /// Cap on distinct stacks merged into one flame graph. The heaviest are kept; the query asks
    /// for one more so the caller can tell the cap was hit.
    /// </summary>
    public const int MaxStacks = 5000;

    /// <summary>Row cap for the types listing.</summary>
    public const int MaxTypes = 500;

    public static int ClampWindowMinutes(int? requested) =>
        requested is > 0 ? Math.Clamp(requested.Value, MinWindowMinutes, MaxWindowMinutes) : DefaultWindowMinutes;

    public static DateTimeOffset ResolveWindowEnd(long? endUnixMs, DateTimeOffset now) =>
        HostInventoryQueryBuilder.ResolveWindowEnd(endUnixMs, now);

    /// <summary>Columns: Service, SampleType, SampleUnit, SampleCount, LastSeenUnixMs.</summary>
    public static ProfileSql BuildTypes(int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = Where(parameters, windowMinutes, end, service: null, sampleType: null, traceId: null, spanId: null);
        parameters.AddParameter("limit", (uint)MaxTypes);

        var sql = "SELECT\n" +
            "    ServiceName AS Service,\n" +
            "    SampleType,\n" +
            "    SampleUnit,\n" +
            "    count() AS SampleCount,\n" +
            "    toUnixTimestamp64Milli(max(Timestamp)) AS LastSeenUnixMs\n" +
            "FROM clickhousedb.profile_samples\n" +
            $"WHERE {where}\n" +
            "GROUP BY Service, SampleType, SampleUnit\n" +
            "ORDER BY Service, SampleType, SampleUnit\n" +
            "LIMIT {limit:UInt32}";
        return new ProfileSql(sql, parameters);
    }

    /// <summary>Columns: Stack (root first), Total, SampleUnit - heaviest stack first.</summary>
    public static ProfileSql BuildFlameGraph(FlameGraphRequest request, int windowMinutes, DateTimeOffset end)
    {
        var parameters = new ClickHouseParameterCollection();
        var where = Where(parameters, windowMinutes, end, request.Service, request.SampleType, request.TraceId, request.SpanId);
        parameters.AddParameter("limit", (uint)(MaxStacks + 1));

        var sql = "SELECT\n" +
            "    Stack,\n" +
            "    sum(Value) AS Total,\n" +
            "    any(SampleUnit) AS SampleUnit\n" +
            "FROM clickhousedb.profile_samples\n" +
            $"WHERE {where}\n" +
            "GROUP BY StackHash, Stack\n" +
            "ORDER BY Total DESC\n" +
            "LIMIT {limit:UInt32}";
        return new ProfileSql(sql, parameters);
    }

    private static string Where(
        ClickHouseParameterCollection parameters,
        int windowMinutes,
        DateTimeOffset end,
        string? service,
        string? sampleType,
        string? traceId,
        string? spanId)
    {
        parameters.AddParameter("from", end.AddMinutes(-windowMinutes).UtcDateTime);
        parameters.AddParameter("to", end.UtcDateTime);

        var clauses = new List<string>
        {
            "Timestamp >= {from:DateTime64(9)}",
            "Timestamp < {to:DateTime64(9)}",
        };

        if (!string.IsNullOrWhiteSpace(service))
        {
            parameters.AddParameter("service", service);
            clauses.Add("ServiceName = {service:String}");
        }

        if (!string.IsNullOrWhiteSpace(sampleType))
        {
            parameters.AddParameter("sampleType", sampleType);
            clauses.Add("SampleType = {sampleType:String}");
        }

        if (!string.IsNullOrWhiteSpace(traceId))
        {
            parameters.AddParameter("traceId", traceId.ToLowerInvariant());
            clauses.Add("TraceId = {traceId:String}");
        }

        if (!string.IsNullOrWhiteSpace(spanId))
        {
            parameters.AddParameter("spanId", spanId.ToLowerInvariant());
            clauses.Add("SpanId = {spanId:String}");
        }

        ServiceScope.Append(clauses, parameters);
        return string.Join(" AND ", clauses);
    }
}
