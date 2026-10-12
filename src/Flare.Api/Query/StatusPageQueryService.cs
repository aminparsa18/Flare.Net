using System.Text.Json;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Json;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IStatusPageQueryService
{
    Task<StatusPage> CreateAsync(StatusPageRequest request, CancellationToken cancellationToken);

    /// <summary>Every (non-deleted) page, enabled or not.</summary>
    Task<IReadOnlyList<StatusPage>> ListAsync(CancellationToken cancellationToken);

    Task<StatusPage?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The page published at <paramref name="slug"/>, or null when there is none or it is disabled.</summary>
    Task<StatusPage?> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken);

    /// <summary>The page published on <paramref name="domain"/> (lower-case host), or null when there is none or it is disabled.</summary>
    Task<StatusPage?> GetPublishedByDomainAsync(string domain, CancellationToken cancellationToken);

    Task<StatusPage?> UpdateAsync(Guid id, StatusPageRequest request, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (inserts a tombstone version) - see 0072_status_pages.sql. Returns false if <paramref name="id"/> doesn't exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for status page CRUD - same role/shape as <see cref="AlertTemplateQueryService"/>
/// (<c>ReplacingMergeTree(UpdatedAt)</c>, tombstone delete, <see cref="LatestVersionSql"/> reads), against <c>status_pages</c>.
/// </summary>
public sealed class StatusPageQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IStatusPageQueryService
{
    private const string Columns = "Id, Slug, Title, Description, Enabled, ComponentsJson, CreatedAt, UpdatedAt, SubscriberChannelIds, Domain, LogoUrl, AccentColor, SupportUrl";

    internal static StatusPage Apply(StatusPage page, StatusPageRequest request) => page with
    {
        Slug = request.Slug.Trim(),
        Title = request.Title.Trim(),
        Description = request.Description?.Trim() ?? "",
        Enabled = request.Enabled ?? false,
        Components = (request.Components ?? []).Select(c => c with { Name = c.Name.Trim() }).ToList(),
        Domain = request.Domain is null ? page.Domain : request.Domain.Trim().ToLowerInvariant(),
        LogoUrl = request.LogoUrl is null ? page.LogoUrl : request.LogoUrl.Trim(),
        AccentColor = request.AccentColor is null ? page.AccentColor : request.AccentColor.Trim().ToLowerInvariant(),
        SupportUrl = request.SupportUrl is null ? page.SupportUrl : request.SupportUrl.Trim(),
        SubscriberChannelIds = request.SubscriberChannelIds is null ? page.SubscriberChannelIds : request.SubscriberChannelIds.Distinct().ToList(),
    };

    public async Task<StatusPage> CreateAsync(StatusPageRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var page = Apply(new StatusPage { Id = Guid.NewGuid(), Slug = "", Title = "", CreatedAt = now, UpdatedAt = now }, request);
        await InsertVersionAsync(page, isDeleted: false, cancellationToken);
        return page;
    }

    public async Task<IReadOnlyList<StatusPage>> ListAsync(CancellationToken cancellationToken)
    {
        var sql = LatestVersionSql.Select("status_pages", Columns, orderBy: "Title ASC");
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        var pages = new List<StatusPage>();
        while (reader.Read())
        {
            pages.Add(ReadPage(reader));
        }

        return pages;
    }

    public async Task<StatusPage?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = LatestVersionSql.Select("status_pages", Columns, idWhere: "Id = {id:UUID}");
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? ReadPage(reader) : null;
    }

    // The table is tiny (a handful of pages), so the slug is matched after the latest-version read
    // rather than in SQL, where it would also match superseded versions.
    public async Task<StatusPage?> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken) =>
        (await ListAsync(cancellationToken)).FirstOrDefault(p => p.Enabled && string.Equals(p.Slug, slug, StringComparison.Ordinal));

    public async Task<StatusPage?> GetPublishedByDomainAsync(string domain, CancellationToken cancellationToken) =>
        domain.Length == 0 ? null : (await ListAsync(cancellationToken)).FirstOrDefault(p => p.Enabled && string.Equals(p.Domain, domain, StringComparison.Ordinal));

    public async Task<StatusPage?> UpdateAsync(Guid id, StatusPageRequest request, CancellationToken cancellationToken)
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

    private async Task InsertVersionAsync(StatusPage page, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", page.Id);
        parameters.AddParameter("slug", page.Slug);
        parameters.AddParameter("title", page.Title);
        parameters.AddParameter("description", page.Description);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("enabled", page.Enabled ? (byte)1 : (byte)0);
        parameters.AddParameter("componentsJson", JsonSerializer.Serialize(page.Components, StatusPagesJsonContext.Default.IReadOnlyListStatusPageComponent));
        parameters.AddParameter("createdAt", page.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", page.UpdatedAt.UtcDateTime);
        parameters.AddParameter("subscribers", page.SubscriberChannelIds.ToArray());
        parameters.AddParameter("domain", page.Domain);
        parameters.AddParameter("logoUrl", page.LogoUrl);
        parameters.AddParameter("accentColor", page.AccentColor);
        parameters.AddParameter("supportUrl", page.SupportUrl);

        const string sql = """
            INSERT INTO status_pages
                (Id, Slug, Title, Description, IsDeleted, Enabled, ComponentsJson, CreatedAt, UpdatedAt, SubscriberChannelIds, Domain, LogoUrl, AccentColor, SupportUrl)
            VALUES
                ({id:UUID}, {slug:String}, {title:String}, {description:String}, {isDeleted:UInt8}, {enabled:UInt8}, {componentsJson:String}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)}, {subscribers:Array(UUID)}, {domain:String}, {logoUrl:String}, {accentColor:String}, {supportUrl:String})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static StatusPage ReadPage(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Slug = reader.GetString(1),
        Title = reader.GetString(2),
        Description = reader.GetString(3),
        Enabled = reader.GetFieldValue<byte>(4) != 0,
        Components = JsonSerializer.Deserialize(reader.GetString(5), StatusPagesJsonContext.Default.IReadOnlyListStatusPageComponent) ?? [],
        CreatedAt = ReadUtc(reader, 6),
        UpdatedAt = ReadUtc(reader, 7),
        SubscriberChannelIds = reader.GetFieldValue<Guid[]>(8),
        Domain = reader.GetString(9),
        LogoUrl = reader.GetString(10),
        AccentColor = reader.GetString(11),
        SupportUrl = reader.GetString(12),
    };

    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
