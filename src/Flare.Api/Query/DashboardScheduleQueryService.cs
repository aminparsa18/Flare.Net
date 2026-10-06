using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IDashboardScheduleQueryService
{
    /// <summary><paramref name="ownerUserId"/> is the creating user, whose access the render runs with (null when auth is disabled).</summary>
    Task<DashboardSchedule> CreateAsync(Guid dashboardId, DashboardScheduleRequest request, Guid? ownerUserId, CancellationToken cancellationToken);

    /// <summary>The (non-deleted) schedules of <paramref name="dashboardId"/>.</summary>
    Task<IReadOnlyList<DashboardSchedule>> ListForDashboardAsync(Guid dashboardId, CancellationToken cancellationToken);

    /// <summary>Enabled schedules whose <see cref="DashboardSchedule.NextRunAt"/> is at or before <paramref name="now"/>.</summary>
    Task<IReadOnlyList<DashboardSchedule>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task<DashboardSchedule?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<DashboardSchedule?> UpdateAsync(Guid id, DashboardScheduleRequest request, CancellationToken cancellationToken);

    /// <summary>Writes a new version of <paramref name="schedule"/> with <see cref="DashboardSchedule.NextRunAt"/> set to <paramref name="nextRunAt"/>.</summary>
    Task<DashboardSchedule> SetNextRunAsync(DashboardSchedule schedule, DateTimeOffset nextRunAt, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (inserts a tombstone version). Returns false if <paramref name="id"/> doesn't exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task AddRunAsync(DashboardReportRun run, CancellationToken cancellationToken);

    /// <summary>The most recent runs of <paramref name="scheduleId"/>, newest first.</summary>
    Task<IReadOnlyList<DashboardReportRun>> ListRunsAsync(Guid scheduleId, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for dashboard schedule CRUD and run history - same role/shape as
/// <see cref="OnCallRotationQueryService"/> (<c>ReplacingMergeTree(UpdatedAt)</c>, tombstone delete,
/// <see cref="LatestVersionSql"/> reads) against <c>dashboard_schedules</c>, plus the append-only
/// <c>dashboard_report_runs</c>. See <c>docs-internal/adr/0142-scheduled-dashboard-reports.md</c>.
/// </summary>
public sealed class DashboardScheduleQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IDashboardScheduleQueryService
{
    private const string Columns = "Id, DashboardId, Name, Enabled, Cron, TimeZone, Recipients, TimeRange, VariableQuery, Format, OwnerUserId, NextRunAt, CreatedAt, UpdatedAt";

    internal static DashboardSchedule Apply(DashboardSchedule schedule, DashboardScheduleRequest request, DateTimeOffset now)
    {
        var cron = request.Cron.Trim();
        var timeZone = request.TimeZone.Trim();
        var changedTiming = cron != schedule.Cron || timeZone != schedule.TimeZone;
        return schedule with
        {
            Name = request.Name.Trim(),
            Enabled = request.Enabled ?? schedule.Enabled,
            Cron = cron,
            TimeZone = timeZone,
            Recipients = string.Join(", ", DashboardScheduleRequest.SplitRecipients(request.Recipients)),
            TimeRange = request.TimeRange ?? "",
            VariableQuery = (request.VariableQuery ?? "").TrimStart('?'),
            Format = request.Format ?? DashboardScheduleRequest.PdfFormat,
            // A new or re-timed schedule waits for its next cron tick rather than firing on save; an edit that
            // leaves the timing alone keeps the pending run (including a "send now").
            NextRunAt = changedTiming || schedule.NextRunAt == default
                ? DashboardScheduleRequest.NextOccurrence(cron, timeZone, now) ?? DateTimeOffset.MaxValue
                : schedule.NextRunAt,
        };
    }

    public async Task<DashboardSchedule> CreateAsync(Guid dashboardId, DashboardScheduleRequest request, Guid? ownerUserId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var schedule = Apply(
            new DashboardSchedule
            {
                Id = Guid.NewGuid(),
                DashboardId = dashboardId,
                Name = "",
                Cron = "",
                TimeZone = "",
                Recipients = "",
                OwnerUserId = ownerUserId,
                NextRunAt = default,
                CreatedAt = now,
                UpdatedAt = now,
            },
            request,
            now);
        await InsertVersionAsync(schedule, isDeleted: false, cancellationToken);
        return schedule;
    }

    public Task<IReadOnlyList<DashboardSchedule>> ListForDashboardAsync(Guid dashboardId, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("dashboardId", dashboardId);
        var sql = LatestVersionSql.Select("dashboard_schedules", Columns, latestWhere: "DashboardId = {dashboardId:UUID}", orderBy: "Name ASC");
        return ReadAsync(sql, parameters, cancellationToken);
    }

    public Task<IReadOnlyList<DashboardSchedule>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("now", now.UtcDateTime);
        var sql = LatestVersionSql.Select("dashboard_schedules", Columns, latestWhere: "Enabled = 1 AND NextRunAt <= {now:DateTime64(3)}", orderBy: "NextRunAt ASC");
        return ReadAsync(sql, parameters, cancellationToken);
    }

    public async Task<DashboardSchedule?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = LatestVersionSql.Select("dashboard_schedules", Columns, idWhere: "Id = {id:UUID}");
        var rows = await ReadAsync(sql, parameters, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<DashboardSchedule?> UpdateAsync(Guid id, DashboardScheduleRequest request, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var updated = Apply(existing, request, now) with { UpdatedAt = now };
        await InsertVersionAsync(updated, isDeleted: false, cancellationToken);
        return updated;
    }

    public async Task<DashboardSchedule> SetNextRunAsync(DashboardSchedule schedule, DateTimeOffset nextRunAt, CancellationToken cancellationToken)
    {
        var updated = schedule with { NextRunAt = nextRunAt, UpdatedAt = timeProvider.GetUtcNow() };
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

    public async Task AddRunAsync(DashboardReportRun run, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", run.Id);
        parameters.AddParameter("scheduleId", run.ScheduleId);
        parameters.AddParameter("dashboardId", run.DashboardId);
        parameters.AddParameter("startedAt", run.StartedAt.UtcDateTime);
        parameters.AddParameter("durationMs", (uint)Math.Max(0, run.DurationMs));
        parameters.AddParameter("status", run.Status);
        parameters.AddParameter("error", run.Error);
        parameters.AddParameter("recipientCount", (uint)Math.Max(0, run.RecipientCount));
        parameters.AddParameter("sizeBytes", (ulong)Math.Max(0, run.SizeBytes));

        const string sql = """
            INSERT INTO dashboard_report_runs
                (Id, ScheduleId, DashboardId, StartedAt, DurationMs, Status, Error, RecipientCount, SizeBytes)
            VALUES
                ({id:UUID}, {scheduleId:UUID}, {dashboardId:UUID}, {startedAt:DateTime64(3)}, {durationMs:UInt32}, {status:String}, {error:String}, {recipientCount:UInt32}, {sizeBytes:UInt64})
            """;
        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    public async Task<IReadOnlyList<DashboardReportRun>> ListRunsAsync(Guid scheduleId, int limit, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("scheduleId", scheduleId);
        parameters.AddParameter("limit", (uint)Math.Clamp(limit, 1, 200));
        const string sql = """
            SELECT Id, ScheduleId, DashboardId, StartedAt, DurationMs, Status, Error, RecipientCount, SizeBytes
            FROM dashboard_report_runs
            WHERE ScheduleId = {scheduleId:UUID}
            ORDER BY StartedAt DESC
            LIMIT {limit:UInt32}
            """;
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        var runs = new List<DashboardReportRun>();
        while (reader.Read())
        {
            runs.Add(new DashboardReportRun
            {
                Id = reader.GetGuid(0),
                ScheduleId = reader.GetGuid(1),
                DashboardId = reader.GetGuid(2),
                StartedAt = ReadUtc(reader, 3),
                DurationMs = (int)reader.GetFieldValue<uint>(4),
                Status = reader.GetString(5),
                Error = reader.GetString(6),
                RecipientCount = (int)reader.GetFieldValue<uint>(7),
                SizeBytes = (long)reader.GetFieldValue<ulong>(8),
            });
        }

        return runs;
    }

    private async Task<IReadOnlyList<DashboardSchedule>> ReadAsync(string sql, ClickHouseParameterCollection parameters, CancellationToken cancellationToken)
    {
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        var schedules = new List<DashboardSchedule>();
        while (reader.Read())
        {
            schedules.Add(ReadSchedule(reader));
        }

        return schedules;
    }

    private async Task InsertVersionAsync(DashboardSchedule schedule, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", schedule.Id);
        parameters.AddParameter("dashboardId", schedule.DashboardId);
        parameters.AddParameter("name", schedule.Name);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("enabled", schedule.Enabled ? (byte)1 : (byte)0);
        parameters.AddParameter("cron", schedule.Cron);
        parameters.AddParameter("timeZone", schedule.TimeZone);
        parameters.AddParameter("recipients", schedule.Recipients);
        parameters.AddParameter("timeRange", schedule.TimeRange);
        parameters.AddParameter("variableQuery", schedule.VariableQuery);
        parameters.AddParameter("format", schedule.Format);
        parameters.AddParameter("ownerUserId", (object?)schedule.OwnerUserId ?? DBNull.Value);
        parameters.AddParameter("nextRunAt", ClampForDateTime64(schedule.NextRunAt).UtcDateTime);
        parameters.AddParameter("createdAt", schedule.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", schedule.UpdatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO dashboard_schedules
                (Id, DashboardId, Name, IsDeleted, Enabled, Cron, TimeZone, Recipients, TimeRange, VariableQuery, Format, OwnerUserId, NextRunAt, CreatedAt, UpdatedAt)
            VALUES
                ({id:UUID}, {dashboardId:UUID}, {name:String}, {isDeleted:UInt8}, {enabled:UInt8}, {cron:String}, {timeZone:String}, {recipients:String}, {timeRange:String}, {variableQuery:String}, {format:String}, {ownerUserId:Nullable(UUID)}, {nextRunAt:DateTime64(3)}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;
        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    /// <summary>
    /// A cron expression that never fires again (e.g. <c>0 0 30 2 *</c>) has no next run; it is parked at the
    /// end of what <c>DateTime64(3)</c> stores so it is never "due".
    /// </summary>
    private static DateTimeOffset ClampForDateTime64(DateTimeOffset value) =>
        value > Parked ? Parked : value;

    private static readonly DateTimeOffset Parked = new(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DashboardSchedule ReadSchedule(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        DashboardId = reader.GetGuid(1),
        Name = reader.GetString(2),
        Enabled = reader.GetFieldValue<byte>(3) != 0,
        Cron = reader.GetString(4),
        TimeZone = reader.GetString(5),
        Recipients = reader.GetString(6),
        TimeRange = reader.GetString(7),
        VariableQuery = reader.GetString(8),
        Format = reader.GetString(9),
        OwnerUserId = reader.IsDBNull(10) ? null : reader.GetGuid(10),
        NextRunAt = ReadUtc(reader, 11),
        CreatedAt = ReadUtc(reader, 12),
        UpdatedAt = ReadUtc(reader, 13),
    };

    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
