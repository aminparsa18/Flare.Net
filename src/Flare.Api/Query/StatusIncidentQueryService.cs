using System.Text.Json;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Json;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IStatusIncidentQueryService
{
    /// <summary>Every (non-deleted) incident on a page, newest first.</summary>
    Task<IReadOnlyList<StatusIncident>> ListAsync(Guid pageId, CancellationToken cancellationToken);

    Task<StatusIncident?> GetAsync(Guid pageId, Guid id, CancellationToken cancellationToken);

    /// <summary>Inserts a new version (a new incident, or an existing one with an update appended).</summary>
    Task SaveAsync(StatusIncident incident, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (tombstone version). Returns false if the incident doesn't exist on that page.</summary>
    Task<bool> DeleteAsync(Guid pageId, Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for status page incidents - same role/shape as <see cref="StatusPageQueryService"/>,
/// against <c>status_incidents</c> (migration 0073).
/// </summary>
public sealed class StatusIncidentQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IStatusIncidentQueryService
{
    private const string Columns = "Id, PageId, Title, UpdatesJson, CreatedAt, UpdatedAt";

    public async Task<IReadOnlyList<StatusIncident>> ListAsync(Guid pageId, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("pageId", pageId);
        var sql = LatestVersionSql.Select("status_incidents", Columns, idWhere: "PageId = {pageId:UUID}", orderBy: "CreatedAt DESC");
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        var incidents = new List<StatusIncident>();
        while (reader.Read())
        {
            incidents.Add(Read(reader));
        }

        return incidents;
    }

    public async Task<StatusIncident?> GetAsync(Guid pageId, Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("pageId", pageId);
        parameters.AddParameter("id", id);
        var sql = LatestVersionSql.Select("status_incidents", Columns, idWhere: "Id = {id:UUID} AND PageId = {pageId:UUID}");
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? Read(reader) : null;
    }

    public Task SaveAsync(StatusIncident incident, CancellationToken cancellationToken) =>
        InsertVersionAsync(incident, isDeleted: false, cancellationToken);

    public async Task<bool> DeleteAsync(Guid pageId, Guid id, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(pageId, id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        await InsertVersionAsync(existing with { UpdatedAt = timeProvider.GetUtcNow() }, isDeleted: true, cancellationToken);
        return true;
    }

    private async Task InsertVersionAsync(StatusIncident incident, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", incident.Id);
        parameters.AddParameter("pageId", incident.PageId);
        parameters.AddParameter("title", incident.Title);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("updatesJson", JsonSerializer.Serialize(incident.Updates, StatusPagesJsonContext.Default.IReadOnlyListStatusIncidentUpdate));
        parameters.AddParameter("createdAt", incident.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", incident.UpdatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO status_incidents
                (Id, PageId, Title, IsDeleted, UpdatesJson, CreatedAt, UpdatedAt)
            VALUES
                ({id:UUID}, {pageId:UUID}, {title:String}, {isDeleted:UInt8}, {updatesJson:String}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static StatusIncident Read(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        PageId = reader.GetGuid(1),
        Title = reader.GetString(2),
        Updates = JsonSerializer.Deserialize(reader.GetString(3), StatusPagesJsonContext.Default.IReadOnlyListStatusIncidentUpdate) ?? [],
        CreatedAt = ReadUtc(reader, 4),
        UpdatedAt = ReadUtc(reader, 5),
    };

    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
