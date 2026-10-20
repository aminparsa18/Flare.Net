using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface ICheckInMonitorQueryService
{
    /// <summary>Creates a monitor. Returns null when a live monitor already has the request's slug.</summary>
    Task<CheckInMonitor?> CreateAsync(CheckInMonitorRequest request, CancellationToken cancellationToken);

    /// <summary>Every (non-deleted) monitor.</summary>
    Task<IReadOnlyList<CheckInMonitor>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The newest pings per slug among <paramref name="slugs"/>. A slug that never pinged is absent.</summary>
    Task<IReadOnlyDictionary<string, CheckInPings>> PingsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken);

    Task<CheckInMonitor?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<CheckInMonitor?> UpdateAsync(Guid id, CheckInMonitorRequest request, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (inserts a tombstone version) - see 0089_checkin_monitors.sql. Returns false if <paramref name="id"/> doesn't exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for check-in monitor CRUD - same role/shape as <see cref="SyntheticMonitorQueryService"/>
/// (<c>ReplacingMergeTree(UpdatedAt)</c>, tombstone delete, <see cref="LatestVersionSql"/> reads), against
/// <c>checkin_monitors</c>, plus the newest-ping lookup over <c>checkin_events</c>.
/// </summary>
public sealed class CheckInMonitorQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : ICheckInMonitorQueryService
{
    private const string Columns = "Id, Slug, Name, Description, Enabled, CronExpression, IntervalSeconds, TimeZone, GraceSeconds, MaxRuntimeSeconds, CreatedAt, UpdatedAt";

    /// <summary>How far back pings are read. Matches the table's one-year TTL, so a yearly job is still seen.</summary>
    private static readonly TimeSpan PingLookback = TimeSpan.FromDays(366);

    internal static CheckInMonitor Apply(CheckInMonitor monitor, CheckInMonitorRequest request)
    {
        var cron = request.CronExpression?.Trim() ?? "";
        return monitor with
        {
            Name = request.Name.Trim(),
            Description = request.Description ?? "",
            Enabled = request.Enabled ?? true,
            CronExpression = cron,
            IntervalSeconds = cron.Length > 0 ? 0 : request.IntervalSeconds ?? 0,
            TimeZone = cron.Length > 0 && !string.IsNullOrWhiteSpace(request.TimeZone) ? request.TimeZone.Trim() : "UTC",
            GraceSeconds = request.GraceSeconds ?? CheckInMonitorRequest.DefaultGraceSeconds,
            MaxRuntimeSeconds = request.MaxRuntimeSeconds ?? 0,
        };
    }

    public async Task<CheckInMonitor?> CreateAsync(CheckInMonitorRequest request, CancellationToken cancellationToken)
    {
        var slug = request.EffectiveSlug();
        if ((await ListAsync(cancellationToken)).Any(m => m.Slug == slug))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var monitor = Apply(
            new CheckInMonitor { Id = Guid.NewGuid(), Slug = slug, Name = "", CreatedAt = now, UpdatedAt = now },
            request);
        await InsertVersionAsync(monitor, isDeleted: false, cancellationToken);
        return monitor;
    }

    public async Task<IReadOnlyList<CheckInMonitor>> ListAsync(CancellationToken cancellationToken)
    {
        var sql = LatestVersionSql.Select("checkin_monitors", Columns, orderBy: "Name ASC");
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        var monitors = new List<CheckInMonitor>();
        while (reader.Read())
        {
            monitors.Add(ReadMonitor(reader));
        }

        return monitors;
    }

    public async Task<IReadOnlyDictionary<string, CheckInPings>> PingsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken)
    {
        if (slugs.Count == 0)
        {
            return new Dictionary<string, CheckInPings>();
        }

        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("slugs", slugs.ToArray());
        parameters.AddParameter("since", timeProvider.GetUtcNow().Subtract(PingLookback).UtcDateTime);

        // maxIf over no matching rows is the epoch, so the counts say whether each kind of ping exists at all.
        const string sql = """
            SELECT Slug,
                   countIf(Status = 'start') AS Starts, maxIf(Time, Status = 'start') AS LastStart,
                   countIf(Status != 'start') AS Finishes, maxIf(Time, Status != 'start') AS LastFinish,
                   argMaxIf(Status, Time, Status != 'start') AS LastFinishStatus
            FROM checkin_events
            WHERE Slug IN {slugs:Array(String)} AND Time >= {since:DateTime64(3)}
            GROUP BY Slug
            """;

        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        var pings = new Dictionary<string, CheckInPings>();
        while (reader.Read())
        {
            pings[reader.GetString(0)] = new CheckInPings(
                reader.GetFieldValue<ulong>(1) > 0 ? ReadUtc(reader, 2) : null,
                reader.GetFieldValue<ulong>(3) > 0 ? ReadUtc(reader, 4) : null,
                reader.GetString(5) == "fail");
        }

        return pings;
    }

    public async Task<CheckInMonitor?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = LatestVersionSql.Select("checkin_monitors", Columns, idWhere: "Id = {id:UUID}");
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? ReadMonitor(reader) : null;
    }

    public async Task<CheckInMonitor?> UpdateAsync(Guid id, CheckInMonitorRequest request, CancellationToken cancellationToken)
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

    private async Task InsertVersionAsync(CheckInMonitor monitor, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", monitor.Id);
        parameters.AddParameter("slug", monitor.Slug);
        parameters.AddParameter("name", monitor.Name);
        parameters.AddParameter("description", monitor.Description);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("enabled", monitor.Enabled ? (byte)1 : (byte)0);
        parameters.AddParameter("cron", monitor.CronExpression);
        parameters.AddParameter("intervalSeconds", (uint)monitor.IntervalSeconds);
        parameters.AddParameter("timeZone", monitor.TimeZone);
        parameters.AddParameter("graceSeconds", (uint)monitor.GraceSeconds);
        parameters.AddParameter("maxRuntimeSeconds", (uint)monitor.MaxRuntimeSeconds);
        parameters.AddParameter("createdAt", monitor.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", monitor.UpdatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO checkin_monitors
                (Id, Slug, Name, Description, IsDeleted, Enabled, CronExpression, IntervalSeconds, TimeZone, GraceSeconds, MaxRuntimeSeconds, CreatedAt, UpdatedAt)
            VALUES
                ({id:UUID}, {slug:String}, {name:String}, {description:String}, {isDeleted:UInt8}, {enabled:UInt8}, {cron:String}, {intervalSeconds:UInt32}, {timeZone:String}, {graceSeconds:UInt32}, {maxRuntimeSeconds:UInt32}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static CheckInMonitor ReadMonitor(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Slug = reader.GetString(1),
        Name = reader.GetString(2),
        Description = reader.GetString(3),
        Enabled = reader.GetFieldValue<byte>(4) != 0,
        CronExpression = reader.GetString(5),
        IntervalSeconds = (int)reader.GetFieldValue<uint>(6),
        TimeZone = reader.GetString(7) is { Length: > 0 } zone ? zone : "UTC",
        GraceSeconds = (int)reader.GetFieldValue<uint>(8),
        MaxRuntimeSeconds = (int)reader.GetFieldValue<uint>(9),
        CreatedAt = ReadUtc(reader, 10),
        UpdatedAt = ReadUtc(reader, 11),
    };

    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
