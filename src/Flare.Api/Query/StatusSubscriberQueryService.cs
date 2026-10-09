using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IStatusSubscriberQueryService
{
    /// <summary>Every (non-deleted) subscriber of a page, verified or not, oldest first.</summary>
    Task<IReadOnlyList<StatusSubscriber>> ListAsync(Guid pageId, CancellationToken cancellationToken);

    Task<StatusSubscriber?> GetAsync(Guid pageId, Guid id, CancellationToken cancellationToken);

    /// <summary>Inserts a new version of the subscriber.</summary>
    Task SaveAsync(StatusSubscriber subscriber, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (tombstone version). Returns false if the subscriber doesn't exist on that page.</summary>
    Task<bool> DeleteAsync(Guid pageId, Guid id, CancellationToken cancellationToken);
}

/// <summary>The ClickHouse seam for <c>status_subscribers</c> (migration 0076); same shape as <see cref="StatusIncidentQueryService"/>.</summary>
public sealed class StatusSubscriberQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IStatusSubscriberQueryService
{
    private const string Columns = "Id, PageId, Email, Verified, CreatedAt, UpdatedAt";

    public async Task<IReadOnlyList<StatusSubscriber>> ListAsync(Guid pageId, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("pageId", pageId);
        var sql = LatestVersionSql.Select("status_subscribers", Columns, idWhere: "PageId = {pageId:UUID}", orderBy: "CreatedAt ASC");
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        var subscribers = new List<StatusSubscriber>();
        while (reader.Read())
        {
            subscribers.Add(Read(reader));
        }

        return subscribers;
    }

    public async Task<StatusSubscriber?> GetAsync(Guid pageId, Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("pageId", pageId);
        parameters.AddParameter("id", id);
        var sql = LatestVersionSql.Select("status_subscribers", Columns, idWhere: "Id = {id:UUID} AND PageId = {pageId:UUID}");
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? Read(reader) : null;
    }

    public Task SaveAsync(StatusSubscriber subscriber, CancellationToken cancellationToken) =>
        InsertVersionAsync(subscriber, isDeleted: false, cancellationToken);

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

    private async Task InsertVersionAsync(StatusSubscriber subscriber, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", subscriber.Id);
        parameters.AddParameter("pageId", subscriber.PageId);
        parameters.AddParameter("email", subscriber.Email);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("verified", subscriber.Verified ? (byte)1 : (byte)0);
        parameters.AddParameter("createdAt", subscriber.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", subscriber.UpdatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO status_subscribers
                (Id, PageId, Email, IsDeleted, Verified, CreatedAt, UpdatedAt)
            VALUES
                ({id:UUID}, {pageId:UUID}, {email:String}, {isDeleted:UInt8}, {verified:UInt8}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static StatusSubscriber Read(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        PageId = reader.GetGuid(1),
        Email = reader.GetString(2),
        Verified = reader.GetFieldValue<byte>(3) != 0,
        CreatedAt = ReadUtc(reader, 4),
        UpdatedAt = ReadUtc(reader, 5),
    };

    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
