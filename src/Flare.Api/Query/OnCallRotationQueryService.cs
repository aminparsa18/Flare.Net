using System.Text.Json;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Json;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IOnCallRotationQueryService
{
    Task<OnCallRotation> CreateAsync(OnCallRotationRequest request, CancellationToken cancellationToken);

    /// <summary>Every (non-deleted) rotation.</summary>
    Task<IReadOnlyList<OnCallRotation>> ListAsync(CancellationToken cancellationToken);

    Task<OnCallRotation?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<OnCallRotation?> UpdateAsync(Guid id, OnCallRotationRequest request, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (inserts a tombstone version) - see 0055_oncall_rotations.sql. Returns false if <paramref name="id"/> doesn't exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for on-call rotation CRUD - same role/shape as
/// <see cref="MaintenanceWindowQueryService"/> (<c>ReplacingMergeTree(UpdatedAt)</c>, tombstone
/// delete, <see cref="LatestVersionSql"/> reads), against <c>oncall_rotations</c>.
/// </summary>
public sealed class OnCallRotationQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IOnCallRotationQueryService
{
    private const string Columns = "Id, Name, Description, ChannelIds, ShiftHours, StartsAt, CreatedAt, UpdatedAt, Overrides, Coverage";

    internal static OnCallRotation Apply(OnCallRotation rotation, OnCallRotationRequest request) => rotation with
    {
        Name = request.Name.Trim(),
        Description = request.Description ?? "",
        ChannelIds = request.ChannelIds ?? [],
        ShiftHours = request.ShiftHours ?? 168,
        StartsAt = request.StartsAt,
        Overrides = request.Overrides ?? [],
        Coverage = request.Coverage,
    };

    public async Task<OnCallRotation> CreateAsync(OnCallRotationRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var rotation = Apply(
            new OnCallRotation { Id = Guid.NewGuid(), Name = "", StartsAt = default, CreatedAt = now, UpdatedAt = now },
            request);
        await InsertVersionAsync(rotation, isDeleted: false, cancellationToken);
        return rotation;
    }

    public async Task<IReadOnlyList<OnCallRotation>> ListAsync(CancellationToken cancellationToken)
    {
        var sql = LatestVersionSql.Select("oncall_rotations", Columns, orderBy: "Name ASC");
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        var rotations = new List<OnCallRotation>();
        while (reader.Read())
        {
            rotations.Add(ReadRotation(reader));
        }

        return rotations;
    }

    public async Task<OnCallRotation?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = LatestVersionSql.Select("oncall_rotations", Columns, idWhere: "Id = {id:UUID}");
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? ReadRotation(reader) : null;
    }

    public async Task<OnCallRotation?> UpdateAsync(Guid id, OnCallRotationRequest request, CancellationToken cancellationToken)
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

    private async Task InsertVersionAsync(OnCallRotation rotation, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", rotation.Id);
        parameters.AddParameter("name", rotation.Name);
        parameters.AddParameter("description", rotation.Description);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("channelIds", rotation.ChannelIds.ToArray());
        parameters.AddParameter("shiftHours", (uint)rotation.ShiftHours);
        parameters.AddParameter("startsAt", rotation.StartsAt.UtcDateTime);
        parameters.AddParameter("createdAt", rotation.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", rotation.UpdatedAt.UtcDateTime);
        parameters.AddParameter("overrides", JsonSerializer.Serialize(rotation.Overrides.ToArray(), OnCallRotationsJsonContext.Default.OnCallOverrideArray));

        parameters.AddParameter("coverage", rotation.Coverage is null ? "" : JsonSerializer.Serialize(rotation.Coverage, OnCallRotationsJsonContext.Default.OnCallCoverage));

        const string sql = """
            INSERT INTO oncall_rotations
                (Id, Name, Description, IsDeleted, ChannelIds, ShiftHours, StartsAt, CreatedAt, UpdatedAt, Overrides, Coverage)
            VALUES
                ({id:UUID}, {name:String}, {description:String}, {isDeleted:UInt8}, {channelIds:Array(UUID)}, {shiftHours:UInt32}, {startsAt:DateTime64(3)}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)}, {overrides:String}, {coverage:String})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static OnCallRotation ReadRotation(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Description = reader.GetString(2),
        ChannelIds = reader.GetFieldValue<Guid[]>(3),
        ShiftHours = (int)reader.GetFieldValue<uint>(4),
        StartsAt = ReadUtc(reader, 5),
        CreatedAt = ReadUtc(reader, 6),
        UpdatedAt = ReadUtc(reader, 7),
        Overrides = ParseOverrides(reader.GetString(8)),
        Coverage = ParseCoverage(reader.GetString(9)),
    };

    private static OnCallOverride[] ParseOverrides(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, OnCallRotationsJsonContext.Default.OnCallOverrideArray) ?? [];
        }
        catch (JsonException)
        {
            // A hand-edited bad value must not take the rotation (and every rule escalating to it) down.
            return [];
        }
    }

    private static OnCallCoverage? ParseCoverage(string json)
    {
        if (json.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, OnCallRotationsJsonContext.Default.OnCallCoverage);
        }
        catch (JsonException)
        {
            // Same stance as overrides: a bad stored value reads as unrestricted, so paging continues.
            return null;
        }
    }

    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
