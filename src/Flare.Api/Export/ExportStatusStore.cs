using System.Globalization;
using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using StackExchange.Redis;

namespace Flare.Api.Export;

/// <summary>Redis key names for the export status the workers publish and Flare.Api reads (ADR-0157). Flare.Ingest mirrors the forwarding ones.</summary>
public static class ExportStatusKeys
{
    public const string ArchiveStatus = "flare:archive:status";
    public const string ArchiveWorkerField = "_worker";
    public const string ForwardTargets = "flare:forward:targets";

    public static string ForwardStats(string key) => $"flare:forward:stats:{key}";

    public static string ForwardQueue(string key) => $"flare:forward:queue:{key}";
}

/// <summary>What the archive worker reports about itself, stored under <see cref="ExportStatusKeys.ArchiveWorkerField"/>.</summary>
public sealed record ArchiveWorkerState(bool Active, string Source, DateTimeOffset CheckedAt);

public interface IExportStatusStore
{
    Task<ArchiveStatusResponse> ReadArchiveAsync(CancellationToken cancellationToken);

    Task WriteArchiveWorkerAsync(ArchiveWorkerState state);

    Task WriteArchiveTableAsync(ArchiveTableStatus status);

    Task<ForwardingStatusResponse> ReadForwardingAsync(CancellationToken cancellationToken);
}

public sealed class RedisExportStatusStore(IConnectionMultiplexer redis, TimeProvider timeProvider) : IExportStatusStore
{
    /// <summary>A worker that has not reported for this long is treated as off - it polls every few minutes.</summary>
    private static readonly TimeSpan WorkerStale = TimeSpan.FromMinutes(30);

    public async Task<ArchiveStatusResponse> ReadArchiveAsync(CancellationToken cancellationToken)
    {
        var entries = await redis.GetDatabase().HashGetAllAsync(ExportStatusKeys.ArchiveStatus);
        ArchiveWorkerState? worker = null;
        var tables = new List<ArchiveTableStatus>();
        foreach (var entry in entries)
        {
            try
            {
                if (entry.Name == ExportStatusKeys.ArchiveWorkerField)
                {
                    worker = JsonSerializer.Deserialize(entry.Value.ToString(), ExportStatusJsonContext.Default.ArchiveWorkerState);
                }
                else if (JsonSerializer.Deserialize(entry.Value.ToString(), TelemetryExportJsonContext.Default.ArchiveTableStatus) is { } table)
                {
                    tables.Add(table);
                }
            }
            catch (JsonException)
            {
                // A malformed field is skipped rather than failing the whole status read.
            }
        }

        var active = worker is { Active: true } w && timeProvider.GetUtcNow() - w.CheckedAt < WorkerStale;
        return new ArchiveStatusResponse
        {
            Active = active,
            Source = active ? worker!.Source : "",
            CheckedAt = worker?.CheckedAt,
            Tables = tables.OrderBy(t => t.Table, StringComparer.Ordinal).ToList(),
        };
    }

    public Task WriteArchiveWorkerAsync(ArchiveWorkerState state) =>
        redis.GetDatabase().HashSetAsync(ExportStatusKeys.ArchiveStatus, ExportStatusKeys.ArchiveWorkerField,
            JsonSerializer.Serialize(state, ExportStatusJsonContext.Default.ArchiveWorkerState));

    public Task WriteArchiveTableAsync(ArchiveTableStatus status) =>
        redis.GetDatabase().HashSetAsync(ExportStatusKeys.ArchiveStatus, status.Table,
            JsonSerializer.Serialize(status, TelemetryExportJsonContext.Default.ArchiveTableStatus));

    public async Task<ForwardingStatusResponse> ReadForwardingAsync(CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();
        var keys = await db.SetMembersAsync(ExportStatusKeys.ForwardTargets);
        var targets = new List<ForwardingTargetStatus>(keys.Length);
        foreach (var keyValue in keys)
        {
            var key = keyValue.ToString();
            var fields = (await db.HashGetAllAsync(ExportStatusKeys.ForwardStats(key))).ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
            if (!fields.TryGetValue("name", out var name))
            {
                continue;
            }

            targets.Add(new ForwardingTargetStatus
            {
                Key = key,
                Name = name,
                Source = fields.GetValueOrDefault("source", "config"),
                Pending = await db.StreamLengthAsync(ExportStatusKeys.ForwardQueue(key)),
                Sent = Long(fields, "sent"),
                Failed = Long(fields, "failed"),
                LastSuccessAt = Time(fields, "lastSuccessAt"),
                LastError = fields.GetValueOrDefault("lastError") is { Length: > 0 } e ? e : null,
                LastErrorAt = Time(fields, "lastErrorAt"),
            });
        }

        return new ForwardingStatusResponse { Targets = targets.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList() };
    }

    private static long Long(Dictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var v) && long.TryParse(v, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static DateTimeOffset? Time(Dictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var v) && long.TryParse(v, CultureInfo.InvariantCulture, out var ms) && ms > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;
}
