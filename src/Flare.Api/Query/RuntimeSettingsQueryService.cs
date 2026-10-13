using System.Text.Json;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

/// <summary>Reads and writes the <c>runtime_settings</c> table (ADR-0210).</summary>
public interface IRuntimeSettingsQueryService
{
    Task<PiiRedactionSetting> GetPiiRedactionAsync(CancellationToken cancellationToken);

    Task<PiiRedactionSetting> SetPiiRedactionAsync(bool enabled, string updatedBy, CancellationToken cancellationToken);

    /// <summary>Tombstones the override so Ingest follows its configuration again. False when none was saved.</summary>
    Task<bool> ClearPiiRedactionAsync(string updatedBy, CancellationToken cancellationToken);
}

/// <summary>
/// One fixed <c>Id</c> per key, so saving again is a new version of the same row (same singleton shape as
/// <see cref="TelemetryExportQueryService.ArchiveId"/>). <c>Flare.Ingest</c> polls the same row through its own
/// mirrored query (<c>ClickHousePipelineRuleStore</c>).
/// </summary>
public sealed class RuntimeSettingsQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IRuntimeSettingsQueryService
{
    private const string Columns = "ValueJson, UpdatedBy, UpdatedAt";
    public const string PiiEnabledKey = "pii.enabled";

    public static readonly Guid PiiEnabledId = new("d6f0c5a1-0000-4000-8000-00000000f11e");

    public async Task<PiiRedactionSetting> GetPiiRedactionAsync(CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", PiiEnabledId);
        var sql = LatestVersionSql.Select("runtime_settings", Columns, idWhere: "Id = {id:UUID}");
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
        return reader.Read() ? Read(reader) : new PiiRedactionSetting();
    }

    public async Task<PiiRedactionSetting> SetPiiRedactionAsync(bool enabled, string updatedBy, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await InsertAsync(PiiEnabledId, PiiEnabledKey, JsonSerializer.Serialize(enabled), isDeleted: false, updatedBy, now, cancellationToken);
        return new PiiRedactionSetting { Enabled = enabled, UpdatedAt = now, UpdatedBy = updatedBy };
    }

    public async Task<bool> ClearPiiRedactionAsync(string updatedBy, CancellationToken cancellationToken)
    {
        if ((await GetPiiRedactionAsync(cancellationToken)).Enabled is null)
        {
            return false;
        }

        await InsertAsync(PiiEnabledId, PiiEnabledKey, "null", isDeleted: true, updatedBy, timeProvider.GetUtcNow(), cancellationToken);
        return true;
    }

    /// <summary>A stored value that is not a JSON boolean reads as "no override", matching Ingest.</summary>
    internal static bool? ParseBool(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<bool?>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PiiRedactionSetting Read(ClickHouseDataReader reader) => new()
    {
        Enabled = ParseBool(reader.GetString(0)),
        UpdatedBy = reader.GetString(1),
        UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc)),
    };

    private async Task InsertAsync(Guid id, string key, string valueJson, bool isDeleted, string updatedBy, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        parameters.AddParameter("key", key);
        parameters.AddParameter("valueJson", valueJson);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("updatedBy", updatedBy);
        parameters.AddParameter("updatedAt", updatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO runtime_settings (Id, Key, ValueJson, IsDeleted, UpdatedBy, UpdatedAt)
            VALUES ({id:UUID}, {key:String}, {valueJson:String}, {isDeleted:UInt8}, {updatedBy:String}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
    }
}
