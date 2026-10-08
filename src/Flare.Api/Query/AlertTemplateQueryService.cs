using System.Text.Json;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Json;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IAlertTemplateQueryService
{
    Task<AlertTemplate> CreateAsync(AlertTemplateRequest request, CancellationToken cancellationToken);

    /// <summary>Every (non-deleted) template - also what notification sends read to resolve wording.</summary>
    Task<IReadOnlyList<AlertTemplate>> ListAsync(CancellationToken cancellationToken);

    Task<AlertTemplate?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<AlertTemplate?> UpdateAsync(Guid id, AlertTemplateRequest request, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (inserts a tombstone version) - see 0070_alert_templates.sql. Returns false if <paramref name="id"/> doesn't exist.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// The ClickHouse seam for alert-template CRUD - same role/shape as
/// <see cref="MaintenanceWindowQueryService"/>, against <c>alert_templates</c>. Setting
/// <see cref="AlertTemplate.IsDefault"/> clears it on every other template, so there is at most one.
/// </summary>
public sealed class AlertTemplateQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IAlertTemplateQueryService
{
    private const string TemplateColumns =
        "Id, Name, Description, IsDefault, TitleTemplate, BodyTemplate, ResolvedBodyTemplate, ChannelBodiesJson, CreatedAt, UpdatedAt";

    internal static AlertTemplate Apply(AlertTemplate template, AlertTemplateRequest request) => template with
    {
        Name = request.Name.Trim(),
        Description = request.Description ?? "",
        IsDefault = request.IsDefault ?? false,
        TitleTemplate = request.TitleTemplate ?? "",
        BodyTemplate = request.BodyTemplate ?? "",
        ResolvedBodyTemplate = request.ResolvedBodyTemplate ?? "",
        // Empty entries carry no meaning, so they aren't stored.
        ChannelBodies = (request.ChannelBodies ?? new Dictionary<string, string>())
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value),
    };

    public async Task<AlertTemplate> CreateAsync(AlertTemplateRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var template = Apply(new AlertTemplate { Id = Guid.NewGuid(), Name = "", CreatedAt = now, UpdatedAt = now }, request);
        await InsertVersionAsync(template, isDeleted: false, cancellationToken);
        await ClearOtherDefaultsAsync(template, cancellationToken);
        return template;
    }

    public async Task<IReadOnlyList<AlertTemplate>> ListAsync(CancellationToken cancellationToken)
    {
        var sql = LatestVersionSql.Select("alert_templates", TemplateColumns, orderBy: "Name");
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        var templates = new List<AlertTemplate>();
        while (reader.Read())
        {
            templates.Add(ReadTemplate(reader));
        }

        return templates;
    }

    public async Task<AlertTemplate?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = LatestVersionSql.Select("alert_templates", TemplateColumns, idWhere: "Id = {id:UUID}");
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? ReadTemplate(reader) : null;
    }

    public async Task<AlertTemplate?> UpdateAsync(Guid id, AlertTemplateRequest request, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var updated = Apply(existing, request) with { UpdatedAt = timeProvider.GetUtcNow() };
        await InsertVersionAsync(updated, isDeleted: false, cancellationToken);
        await ClearOtherDefaultsAsync(updated, cancellationToken);
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

    /// <summary>Re-inserts every other default as a non-default version, so saving a new default demotes the old one.</summary>
    private async Task ClearOtherDefaultsAsync(AlertTemplate saved, CancellationToken cancellationToken)
    {
        if (!saved.IsDefault)
        {
            return;
        }

        foreach (var other in (await ListAsync(cancellationToken)).Where(t => t.IsDefault && t.Id != saved.Id))
        {
            // UpdatedAt must move forward or ReplacingMergeTree could keep the old version.
            await InsertVersionAsync(other with { IsDefault = false, UpdatedAt = timeProvider.GetUtcNow() }, isDeleted: false, cancellationToken);
        }
    }

    private async Task InsertVersionAsync(AlertTemplate template, bool isDeleted, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", template.Id);
        parameters.AddParameter("name", template.Name);
        parameters.AddParameter("description", template.Description);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("isDefault", template.IsDefault ? (byte)1 : (byte)0);
        parameters.AddParameter("titleTemplate", template.TitleTemplate);
        parameters.AddParameter("bodyTemplate", template.BodyTemplate);
        parameters.AddParameter("resolvedBodyTemplate", template.ResolvedBodyTemplate);
        parameters.AddParameter("channelBodiesJson", JsonSerializer.Serialize(template.ChannelBodies, AlertTemplatesJsonContext.Default.IReadOnlyDictionaryStringString));
        parameters.AddParameter("createdAt", template.CreatedAt.UtcDateTime);
        parameters.AddParameter("updatedAt", template.UpdatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO alert_templates
                (Id, Name, Description, IsDeleted, IsDefault, TitleTemplate, BodyTemplate, ResolvedBodyTemplate, ChannelBodiesJson, CreatedAt, UpdatedAt)
            VALUES
                ({id:UUID}, {name:String}, {description:String}, {isDeleted:UInt8}, {isDefault:UInt8}, {titleTemplate:String}, {bodyTemplate:String}, {resolvedBodyTemplate:String}, {channelBodiesJson:String}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static AlertTemplate ReadTemplate(ClickHouseDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Description = reader.GetString(2),
        IsDefault = reader.GetByte(3) != 0,
        TitleTemplate = reader.GetString(4),
        BodyTemplate = reader.GetString(5),
        ResolvedBodyTemplate = reader.GetString(6),
        ChannelBodies = JsonSerializer.Deserialize(reader.GetString(7), AlertTemplatesJsonContext.Default.IReadOnlyDictionaryStringString) ?? new Dictionary<string, string>(),
        CreatedAt = ReadUtc(reader, 8),
        UpdatedAt = ReadUtc(reader, 9),
    };

    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
