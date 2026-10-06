using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface ISyntheticMonitorQueryService
{
    Task<SyntheticMonitor> CreateAsync(SyntheticMonitorRequest request, CancellationToken cancellationToken);

    /// <summary>Every (non-deleted) monitor.</summary>
    Task<IReadOnlyList<SyntheticMonitor>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Each monitor's latest probe result per location, keyed by monitor name (the <c>monitor</c> metric attribute). Monitors with no recent result are absent.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<SyntheticLocationStatus>>> LatestStatusesAsync(CancellationToken cancellationToken);

    Task<SyntheticMonitor?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<SyntheticMonitor?> UpdateAsync(Guid id, SyntheticMonitorRequest request, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (inserts a tombstone version) - see 0056_synthetic_monitors.sql. Returns false if <paramref name="id"/> doesn't exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for synthetic monitor CRUD - same role/shape as <see cref="OnCallRotationQueryService"/>
/// (<c>ReplacingMergeTree(UpdatedAt)</c>, tombstone delete, <see cref="LatestVersionSql"/> reads), against
/// <c>synthetic_monitors</c>.
/// </summary>
public sealed class SyntheticMonitorQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : ISyntheticMonitorQueryService
{
    private const string Columns = "Id, Name, Description, Enabled, Kind, Target, Method, ExpectedStatus, IntervalSeconds, TimeoutSeconds, CreatedAt, UpdatedAt, RequestHeaders, RequestBody, BodyContains, BodyNotContains, Locations, BodyMatchesRegex, JsonPath, JsonPathEquals, ExpectedAnswer";

    internal static SyntheticMonitor Apply(SyntheticMonitor monitor, SyntheticMonitorRequest request) => monitor with
    {
        Name = request.Name.Trim(),
        Description = request.Description ?? "",
        Enabled = request.Enabled ?? true,
        Kind = request.Kind ?? SyntheticMonitorKind.Http,
        Target = request.Target.Trim(),
        Method = (request.Method is { Length: > 0 } m ? m : request.Kind == SyntheticMonitorKind.Dns ? "A" : "GET").ToUpperInvariant(),
        ExpectedStatus = request.ExpectedStatus ?? 0,
        RequestHeaders = request.RequestHeaders?.Trim() ?? "",
        RequestBody = request.RequestBody ?? "",
        BodyContains = request.BodyContains ?? "",
        BodyNotContains = request.BodyNotContains ?? "",
        BodyMatchesRegex = request.BodyMatchesRegex ?? "",
        JsonPath = request.JsonPath?.Trim() ?? "",
        JsonPathEquals = request.JsonPathEquals ?? "",
        ExpectedAnswer = request.ExpectedAnswer?.Trim() ?? "",
        IntervalSeconds = request.IntervalSeconds ?? 60,
        TimeoutSeconds = request.TimeoutSeconds ?? 10,
        Locations = request.NormalizedLocations(),
    };

    public async Task<SyntheticMonitor> CreateAsync(SyntheticMonitorRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var monitor = Apply(
            new SyntheticMonitor { Id = Guid.NewGuid(), Name = "", Target = "", CreatedAt = now, UpdatedAt = now },
            request);
        await InsertVersionAsync(monitor, isDeleted: false, cancellationToken);
        return monitor;
    }

    public async Task<IReadOnlyList<SyntheticMonitor>> ListAsync(CancellationToken cancellationToken)
    {
        var sql = LatestVersionSql.Select("synthetic_monitors", Columns, orderBy: "Name ASC");
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        var monitors = new List<SyntheticMonitor>();
        while (reader.Read())
        {
            monitors.Add(ReadMonitor(reader));
        }

        return monitors;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<SyntheticLocationStatus>>> LatestStatusesAsync(CancellationToken cancellationToken)
    {
        // The longest allowed interval is 24 h, so two days always covers a monitor that is still running.
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("service", SyntheticMetrics.ServiceName);
        parameters.AddParameter("since", timeProvider.GetUtcNow().AddDays(-2).UtcDateTime);
        const string sql = """
            SELECT DataPointAttributes['monitor'] AS Monitor, DataPointAttributes['location'] AS Location, MetricName, max(Time) AS LastTime, argMax(Value, Time) AS LastValue
            FROM metrics_gauge
            WHERE ServiceName = {service:String}
              AND Time >= {since:DateTime64(3)}
              AND MetricName IN ('synthetic.up', 'synthetic.duration', 'synthetic.http.status_code', 'synthetic.cert.expiry_days')
            GROUP BY Monitor, Location, MetricName
            """;

        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        var byMonitorLocation = new Dictionary<(string Monitor, string Location), List<SyntheticMonitorStatus.Point>>();
        while (reader.Read())
        {
            // Points written before locations existed carry no location attribute.
            var location = reader.GetString(1) is { Length: > 0 } named ? named : SyntheticMetrics.DefaultLocation;
            var key = (reader.GetString(0), location);
            if (!byMonitorLocation.TryGetValue(key, out var points))
            {
                byMonitorLocation[key] = points = [];
            }

            points.Add(new SyntheticMonitorStatus.Point(reader.GetString(2), ReadUtc(reader, 3), reader.GetDouble(4)));
        }

        var statuses = new Dictionary<string, List<SyntheticLocationStatus>>();
        foreach (var ((monitor, location), points) in byMonitorLocation)
        {
            if (SyntheticMonitorStatus.FromPoints(points) is not { } status)
            {
                continue;
            }

            if (!statuses.TryGetValue(monitor, out var list))
            {
                statuses[monitor] = list = [];
            }

            list.Add(new SyntheticLocationStatus(location, status));
        }

        return statuses.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<SyntheticLocationStatus>)kv.Value.OrderBy(l => l.Location, StringComparer.Ordinal).ToList());
    }

    public async Task<SyntheticMonitor?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = LatestVersionSql.Select("synthetic_monitors", Columns, idWhere: "Id = {id:UUID}");
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? ReadMonitor(reader) : null;
    }

    public async Task<SyntheticMonitor?> UpdateAsync(Guid id, SyntheticMonitorRequest request, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var updated = Apply(existing, request) with { UpdatedAt = timeProvider.GetUtcNow() };
        await InsertVersionAsync(updated, isDeleted: false, cancellationToken);
        return updated;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        await InsertVersionAsync(existing with { UpdatedAt = timeProvider.GetUtcNow() }, isDeleted: true, cancellationToken);
        return true;
    }

    private async Task InsertVersionAsync(SyntheticMonitor monitor, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", monitor.Id);
        parameters.AddParameter("name", monitor.Name);
        parameters.AddParameter("description", monitor.Description);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("enabled", monitor.Enabled ? (byte)1 : (byte)0);
        parameters.AddParameter("kind", monitor.Kind.ToString());
        parameters.AddParameter("target", monitor.Target);
        parameters.AddParameter("method", monitor.Method);
        parameters.AddParameter("expectedStatus", (ushort)monitor.ExpectedStatus);
        parameters.AddParameter("intervalSeconds", (uint)monitor.IntervalSeconds);
        parameters.AddParameter("timeoutSeconds", (uint)monitor.TimeoutSeconds);
        parameters.AddParameter("requestHeaders", monitor.RequestHeaders);
        parameters.AddParameter("requestBody", monitor.RequestBody);
        parameters.AddParameter("bodyContains", monitor.BodyContains);
        parameters.AddParameter("bodyNotContains", monitor.BodyNotContains);
        parameters.AddParameter("bodyMatchesRegex", monitor.BodyMatchesRegex);
        parameters.AddParameter("jsonPath", monitor.JsonPath);
        parameters.AddParameter("jsonPathEquals", monitor.JsonPathEquals);
        parameters.AddParameter("expectedAnswer", monitor.ExpectedAnswer);
        parameters.AddParameter("locations", monitor.Locations.ToArray());
        parameters.AddParameter("createdAt", monitor.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", monitor.UpdatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO synthetic_monitors
                (Id, Name, Description, IsDeleted, Enabled, Kind, Target, Method, ExpectedStatus, IntervalSeconds, TimeoutSeconds, CreatedAt, UpdatedAt, RequestHeaders, RequestBody, BodyContains, BodyNotContains, Locations, BodyMatchesRegex, JsonPath, JsonPathEquals, ExpectedAnswer)
            VALUES
                ({id:UUID}, {name:String}, {description:String}, {isDeleted:UInt8}, {enabled:UInt8}, {kind:String}, {target:String}, {method:String}, {expectedStatus:UInt16}, {intervalSeconds:UInt32}, {timeoutSeconds:UInt32}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)}, {requestHeaders:String}, {requestBody:String}, {bodyContains:String}, {bodyNotContains:String}, {locations:Array(String)}, {bodyMatchesRegex:String}, {jsonPath:String}, {jsonPathEquals:String}, {expectedAnswer:String})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static SyntheticMonitor ReadMonitor(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Description = reader.GetString(2),
        Enabled = reader.GetFieldValue<byte>(3) != 0,
        Kind = Enum.TryParse<SyntheticMonitorKind>(reader.GetString(4), out var kind) ? kind : SyntheticMonitorKind.Http,
        Target = reader.GetString(5),
        Method = reader.GetString(6) is { Length: > 0 } method ? method : "GET",
        ExpectedStatus = reader.GetFieldValue<ushort>(7),
        IntervalSeconds = (int)reader.GetFieldValue<uint>(8),
        TimeoutSeconds = (int)reader.GetFieldValue<uint>(9),
        CreatedAt = ReadUtc(reader, 10),
        UpdatedAt = ReadUtc(reader, 11),
        RequestHeaders = reader.GetString(12),
        RequestBody = reader.GetString(13),
        BodyContains = reader.GetString(14),
        BodyNotContains = reader.GetString(15),
        Locations = reader.GetFieldValue<string[]>(16),
        BodyMatchesRegex = reader.GetString(17),
        JsonPath = reader.GetString(18),
        JsonPathEquals = reader.GetString(19),
        ExpectedAnswer = reader.GetString(20),
    };

    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
