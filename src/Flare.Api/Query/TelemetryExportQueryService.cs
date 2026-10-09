using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using System.Text.Json;
using Flare.Api.Alerting;
using Flare.Api.Json;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

/// <summary>The <c>ConfigJson</c> of a forwarding row. Flare.Ingest's <c>ClickHouseForwardingTargetStore</c> mirrors this shape.</summary>
public sealed record ForwardingConfig
{
    public string Endpoint { get; init; } = "";

    public Dictionary<string, string> Headers { get; init; } = [];

    public List<TelemetrySignal> Signals { get; init; } = [];

    public List<string> Services { get; init; } = [];

    public List<Guid> IngestKeyIds { get; init; } = [];

    public bool Gzip { get; init; } = true;
}

/// <summary>The <c>ConfigJson</c> of the archive row. Flare.AlertWorker reads it through this service.</summary>
public sealed record ArchiveConfig
{
    public string Endpoint { get; init; } = "";

    public string AccessKey { get; init; } = "";

    public string SecretKey { get; init; } = "";

    public string Prefix { get; init; } = "flare";

    public ArchiveFileFormat Format { get; init; } = ArchiveFileFormat.Parquet;

    public List<TelemetrySignal> Signals { get; init; } = [];
}

public interface ITelemetryExportQueryService
{
    Task<IReadOnlyList<ForwardingTarget>> ListForwardingAsync(CancellationToken cancellationToken);

    Task<ForwardingTarget?> GetForwardingAsync(Guid id, CancellationToken cancellationToken);

    Task<ForwardingTarget> CreateForwardingAsync(ForwardingTargetRequest request, CancellationToken cancellationToken);

    Task<ForwardingTarget?> UpdateForwardingAsync(Guid id, ForwardingTargetRequest request, CancellationToken cancellationToken);

    /// <summary>Soft-deletes (tombstone version) - see 0071_telemetry_exports.sql. False if <paramref name="id"/> doesn't exist.</summary>
    Task<bool> DeleteForwardingAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The saved archive settings with real credentials, or null when none are saved. For the worker; endpoints mask them.</summary>
    Task<ArchiveSettings?> GetArchiveAsync(CancellationToken cancellationToken);

    Task<ArchiveSettings> SaveArchiveAsync(ArchiveSettingsRequest request, CancellationToken cancellationToken);

    /// <summary>Removes the saved settings so the archive follows configuration again. False if none were saved.</summary>
    Task<bool> DeleteArchiveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// ClickHouse seam for <c>telemetry_exports</c> - managed OTLP forwarding targets (Kind 0) and the single S3
/// archive row (Kind 1). Same tombstone/latest-version shape as <see cref="MetricAttributeRuleQueryService"/>.
/// Returns the real credentials; masking is the endpoints' job (<see cref="TelemetryExportMasking"/>).
/// </summary>
public sealed class TelemetryExportQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : ITelemetryExportQueryService
{
    private const string Columns = "Id, Name, Enabled, ConfigJson, CreatedAt, UpdatedAt";
    private const byte ForwardingKind = 0;
    private const byte ArchiveKind = 1;

    /// <summary>The archive is a singleton: one fixed id, so saving again is a new version of the same row.</summary>
    public static readonly Guid ArchiveId = new("0a2c1e57-0000-4000-8000-00000000a2c1");

    public async Task<IReadOnlyList<ForwardingTarget>> ListForwardingAsync(CancellationToken cancellationToken)
    {
        var sql = LatestVersionSql.Select("telemetry_exports", Columns, idWhere: $"Kind = {ForwardingKind}", orderBy: "Name");
        await using var reader = await client.ExecuteReaderAsync(sql, null, SafetyOptions(), cancellationToken);
        var targets = new List<ForwardingTarget>();
        while (reader.Read())
        {
            targets.Add(ReadTarget(reader));
        }

        return targets;
    }

    public async Task<ForwardingTarget?> GetForwardingAsync(Guid id, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        var sql = LatestVersionSql.Select("telemetry_exports", Columns, idWhere: $"Id = {{id:UUID}} AND Kind = {ForwardingKind}");
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        return reader.Read() ? ReadTarget(reader) : null;
    }

    public async Task<ForwardingTarget> CreateForwardingAsync(ForwardingTargetRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var target = Build(Guid.NewGuid(), request, existing: null, createdAt: now, updatedAt: now);
        await InsertAsync(target.Id, ForwardingKind, target.Name, target.Enabled, isDeleted: false, ToJson(target), target.CreatedAt, target.UpdatedAt, cancellationToken);
        return target;
    }

    public async Task<ForwardingTarget?> UpdateForwardingAsync(Guid id, ForwardingTargetRequest request, CancellationToken cancellationToken)
    {
        var existing = await GetForwardingAsync(id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var updated = Build(id, request, existing, existing.CreatedAt, timeProvider.GetUtcNow());
        await InsertAsync(id, ForwardingKind, updated.Name, updated.Enabled, isDeleted: false, ToJson(updated), updated.CreatedAt, updated.UpdatedAt, cancellationToken);
        return updated;
    }

    public async Task<bool> DeleteForwardingAsync(Guid id, CancellationToken cancellationToken)
    {
        var existing = await GetForwardingAsync(id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        await InsertAsync(id, ForwardingKind, existing.Name, existing.Enabled, isDeleted: true, ToJson(existing), existing.CreatedAt, timeProvider.GetUtcNow(), cancellationToken);
        return true;
    }

    public async Task<ArchiveSettings?> GetArchiveAsync(CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", ArchiveId);
        var sql = LatestVersionSql.Select("telemetry_exports", Columns, idWhere: $"Id = {{id:UUID}} AND Kind = {ArchiveKind}");
        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);
        if (!reader.Read())
        {
            return null;
        }

        var config = JsonSerializer.Deserialize(reader.GetString(3), TelemetryExportJsonContext.Default.ArchiveConfig) ?? new ArchiveConfig();
        return new ArchiveSettings
        {
            Enabled = reader.GetByte(2) != 0,
            Endpoint = config.Endpoint,
            AccessKey = config.AccessKey,
            SecretKey = config.SecretKey,
            Prefix = config.Prefix,
            Format = config.Format,
            Signals = config.Signals,
            Saved = true,
            UpdatedAt = ReadUtc(reader, 5),
        };
    }

    public async Task<ArchiveSettings> SaveArchiveAsync(ArchiveSettingsRequest request, CancellationToken cancellationToken)
    {
        var existing = await GetArchiveAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var settings = new ArchiveSettings
        {
            Enabled = request.Enabled ?? true,
            Endpoint = request.Endpoint.Trim(),
            AccessKey = TelemetryExportMasking.Restore(request.AccessKey, existing?.AccessKey ?? "")?.Trim() ?? existing?.AccessKey ?? "",
            SecretKey = TelemetryExportMasking.Restore(request.SecretKey, existing?.SecretKey ?? "")?.Trim() ?? existing?.SecretKey ?? "",
            Prefix = (request.Prefix ?? "flare").Trim().Trim('/'),
            Format = request.Format ?? ArchiveFileFormat.Parquet,
            Signals = (request.Signals ?? []).Distinct().ToList(),
            Saved = true,
            UpdatedAt = now,
        };

        var config = new ArchiveConfig
        {
            Endpoint = settings.Endpoint,
            AccessKey = settings.AccessKey,
            SecretKey = settings.SecretKey,
            Prefix = settings.Prefix,
            Format = settings.Format,
            Signals = [.. settings.Signals],
        };
        await InsertAsync(ArchiveId, ArchiveKind, "archive", settings.Enabled, isDeleted: false,
            JsonSerializer.Serialize(config, TelemetryExportJsonContext.Default.ArchiveConfig), existing?.UpdatedAt ?? now, now, cancellationToken);
        return settings;
    }

    public async Task<bool> DeleteArchiveAsync(CancellationToken cancellationToken)
    {
        var existing = await GetArchiveAsync(cancellationToken);
        if (existing is null)
        {
            return false;
        }

        await InsertAsync(ArchiveId, ArchiveKind, "archive", existing.Enabled, isDeleted: true, "{}", existing.UpdatedAt ?? timeProvider.GetUtcNow(), timeProvider.GetUtcNow(), cancellationToken);
        return true;
    }

    private static ForwardingTarget Build(Guid id, ForwardingTargetRequest request, ForwardingTarget? existing, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        var stored = existing?.Headers ?? new Dictionary<string, string>();
        var headers = (request.Headers ?? new Dictionary<string, string>())
            .Where(h => !string.IsNullOrWhiteSpace(h.Key))
            .ToDictionary(h => h.Key.Trim(), h => TelemetryExportMasking.Restore(h.Value, stored.GetValueOrDefault(h.Key.Trim(), "")) ?? "");
        return new ForwardingTarget
        {
            Id = id,
            Name = request.Name.Trim(),
            Enabled = request.Enabled ?? true,
            Endpoint = request.Endpoint.Trim(),
            Headers = headers,
            Signals = (request.Signals ?? []).Distinct().ToList(),
            Services = (request.Services ?? []).Select(s => s.Trim()).Distinct(StringComparer.Ordinal).ToList(),
            IngestKeyIds = (request.IngestKeyIds ?? []).Distinct().ToList(),
            Gzip = request.Gzip ?? true,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    private static string ToJson(ForwardingTarget target) => JsonSerializer.Serialize(new ForwardingConfig
    {
        Endpoint = target.Endpoint,
        Headers = new Dictionary<string, string>(target.Headers),
        Signals = [.. target.Signals],
        Services = [.. target.Services],
        IngestKeyIds = [.. target.IngestKeyIds],
        Gzip = target.Gzip,
    }, TelemetryExportJsonContext.Default.ForwardingConfig);

    private static ForwardingTarget ReadTarget(ClickHouseDataReader reader)
    {
        var config = JsonSerializer.Deserialize(reader.GetString(3), TelemetryExportJsonContext.Default.ForwardingConfig) ?? new ForwardingConfig();
        return new ForwardingTarget
        {
            Id = reader.GetGuid(0),
            Name = reader.GetString(1),
            Enabled = reader.GetByte(2) != 0,
            Endpoint = config.Endpoint,
            Headers = config.Headers,
            Signals = config.Signals,
            Services = config.Services,
            IngestKeyIds = config.IngestKeyIds,
            Gzip = config.Gzip,
            CreatedAt = ReadUtc(reader, 4),
            UpdatedAt = ReadUtc(reader, 5),
        };
    }

    private async Task InsertAsync(Guid id, byte kind, string name, bool enabled, bool isDeleted, string configJson, DateTimeOffset createdAt, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("id", id);
        parameters.AddParameter("kind", kind);
        parameters.AddParameter("name", name);
        parameters.AddParameter("enabled", enabled ? (byte)1 : (byte)0);
        parameters.AddParameter("isDeleted", isDeleted ? (byte)1 : (byte)0);
        parameters.AddParameter("configJson", configJson);
        parameters.AddParameter("createdAt", createdAt.UtcDateTime);
        parameters.AddParameter("updatedAt", updatedAt.UtcDateTime);

        const string sql = """
            INSERT INTO telemetry_exports (Id, Kind, Name, Enabled, IsDeleted, ConfigJson, CreatedAt, UpdatedAt)
            VALUES ({id:UUID}, {kind:UInt8}, {name:String}, {enabled:UInt8}, {isDeleted:UInt8}, {configJson:String}, {createdAt:DateTime64(3)}, {updatedAt:DateTime64(3)})
            """;

        await client.ExecuteNonQueryAsync(sql, parameters, SafetyOptions(), cancellationToken);
    }

    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}

/// <summary>Masks export credentials in responses; shares <see cref="NotificationSecrets"/>' rules so the dashboard round-trips them the same way.</summary>
public static class TelemetryExportMasking
{
    public static string? Restore(string? requested, string stored) => NotificationSecrets.Restore(requested, stored);

    public static ForwardingTarget Redact(ForwardingTarget target) => target with
    {
        Headers = target.Headers.ToDictionary(h => h.Key, h => NotificationSecrets.Mask(h.Value)),
    };

    public static ArchiveSettings Redact(ArchiveSettings settings) => settings with
    {
        AccessKey = NotificationSecrets.Mask(settings.AccessKey),
        SecretKey = NotificationSecrets.Mask(settings.SecretKey),
    };
}
