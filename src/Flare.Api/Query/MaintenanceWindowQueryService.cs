using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IMaintenanceWindowQueryService
{
    Task<MaintenanceWindow> CreateAsync(MaintenanceWindowRequest request, CancellationToken cancellationToken);

    /// <summary>Every (non-deleted) window, active or not - also what <c>AlertEvaluationWorker</c> reads once per tick.</summary>
    Task<IReadOnlyList<MaintenanceWindow>> ListAsync(CancellationToken cancellationToken);

    Task<MaintenanceWindow?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<MaintenanceWindow?> UpdateAsync(Guid id, MaintenanceWindowRequest request, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (inserts a tombstone version) - see 0031_maintenance_windows.sql. Returns false if <paramref name="id"/> doesn't exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for maintenance-window CRUD - same role/shape as
/// <see cref="NotificationChannelQueryService"/> (and <see cref="AlertQueryService"/>'s
/// <c>ReplacingMergeTree(UpdatedAt)</c> / tombstone-delete / <c>FINAL WHERE IsDeleted = 0</c>
/// pattern), against <c>maintenance_windows</c>.
/// </summary>
public sealed class MaintenanceWindowQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IMaintenanceWindowQueryService
{
    private const string WindowColumns =
        "Id, Name, Description, RuleIds, StartsAt, EndsAt, Recurrence, DaysOfWeek, RepeatUntil, TimeZone, CreatedAt, UpdatedAt";

    /// <summary>See <see cref="AlertQueryService.ResolveDefaults"/>'s remarks - same nullable-optional-field coalescing, for <see cref="MaintenanceWindowRequest"/>.</summary>
    internal static MaintenanceWindow Apply(MaintenanceWindow window, MaintenanceWindowRequest request)
    {
        var recurrence = request.Recurrence ?? MaintenanceWindowRecurrence.None;
        return window with
        {
            Name = request.Name.Trim(),
            Description = request.Description ?? "",
            RuleIds = request.RuleIds ?? [],
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            Recurrence = recurrence,
            // Normalized so a stored window never carries fields its recurrence ignores.
            DaysOfWeek = recurrence == MaintenanceWindowRecurrence.Weekly ? (request.DaysOfWeek ?? []).Order().ToList() : [],
            RepeatUntil = recurrence == MaintenanceWindowRecurrence.None ? null : request.RepeatUntil,
            TimeZone = string.IsNullOrWhiteSpace(request.TimeZone) ? "UTC" : request.TimeZone,
        };
    }

    public async Task<MaintenanceWindow> CreateAsync(MaintenanceWindowRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var window = Apply(
            new MaintenanceWindow
            {
                Id = Guid.NewGuid(),
                Name = "",
                StartsAt = default,
                EndsAt = default,
                CreatedAt = now,
                UpdatedAt = now,
            },
            request);

        await InsertWindowVersionAsync(window, isDeleted: false, cancellationToken);
        return window;
    }

    public async Task<IReadOnlyList<MaintenanceWindow>> ListAsync(CancellationToken cancellationToken)
    {
        var sql = $"SELECT {WindowColumns} FROM maintenance_windows FINAL WHERE IsDeleted = 0 ORDER BY StartsAt DESC";
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        var windows = new List<MaintenanceWindow>();
        while (reader.Read())
        {
            windows.Add(ReadWindow(reader));
        }

        return windows;
    }

    public async Task<MaintenanceWindow?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = $"SELECT {WindowColumns} FROM maintenance_windows FINAL WHERE Id = {{id:UUID}} AND IsDeleted = 0";
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? ReadWindow(reader) : null;
    }

    public async Task<MaintenanceWindow?> UpdateAsync(Guid id, MaintenanceWindowRequest request, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var updated = Apply(existing, request) with { UpdatedAt = timeProvider.GetUtcNow() };
        await InsertWindowVersionAsync(updated, isDeleted: false, cancellationToken);
        return updated;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        var tombstone = existing with { UpdatedAt = timeProvider.GetUtcNow() };
        await InsertWindowVersionAsync(tombstone, isDeleted: true, cancellationToken);
        return true;
    }

    private async Task InsertWindowVersionAsync(MaintenanceWindow window, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", window.Id);
        parameters.AddParameter("name", window.Name);
        parameters.AddParameter("description", window.Description);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("ruleIds", window.RuleIds.ToArray());
        parameters.AddParameter("startsAt", window.StartsAt.UtcDateTime);
        parameters.AddParameter("endsAt", window.EndsAt.UtcDateTime);
        parameters.AddParameter("recurrence", window.Recurrence.ToString());
        parameters.AddParameter("daysOfWeek", window.DaysOfWeek.Select(d => (byte)d).ToArray());
        parameters.AddParameter("repeatUntil", (object?)window.RepeatUntil?.UtcDateTime ?? DBNull.Value);
        parameters.AddParameter("timeZone", window.TimeZone);
        parameters.AddParameter("createdAt", window.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", window.UpdatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO maintenance_windows
                (Id, Name, Description, IsDeleted, RuleIds, StartsAt, EndsAt, Recurrence, DaysOfWeek, RepeatUntil, TimeZone, CreatedAt, UpdatedAt)
            VALUES
                ({id:UUID}, {name:String}, {description:String}, {isDeleted:UInt8}, {ruleIds:Array(UUID)}, {startsAt:DateTime64(3)}, {endsAt:DateTime64(3)}, {recurrence:String}, {daysOfWeek:Array(UInt8)}, {repeatUntil:Nullable(DateTime64(3))}, {timeZone:String}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static MaintenanceWindow ReadWindow(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Description = reader.GetString(2),
        RuleIds = reader.GetFieldValue<Guid[]>(3),
        StartsAt = ReadUtc(reader, 4),
        EndsAt = ReadUtc(reader, 5),
        Recurrence = Enum.Parse<MaintenanceWindowRecurrence>(reader.GetString(6)),
        DaysOfWeek = reader.GetFieldValue<byte[]>(7).Select(d => (DayOfWeek)d).ToList(),
        RepeatUntil = reader.IsDBNull(8) ? null : ReadUtc(reader, 8),
        TimeZone = reader.GetString(9),
        CreatedAt = ReadUtc(reader, 10),
        UpdatedAt = ReadUtc(reader, 11),
    };

    /// <summary>See <see cref="LogQueryService"/>'s identical helper's remarks - same <c>DateTime64</c>/<c>Kind=Unspecified</c> driver behavior applies here.</summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    /// <summary>Same query-safety rationale as <see cref="LogQueryService.SafetyOptions"/>, used here for window CRUD.</summary>
    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
