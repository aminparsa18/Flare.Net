using System.Text.Json;
using System.Text.Json.Serialization;
using ClickHouse.Driver;

namespace Flare.Ingest.Forwarding;

/// <summary>A forwarding target saved from the dashboard (ADR-0157), as Flare.Ingest runs it.</summary>
public sealed record ManagedForwardingTarget(Guid Id, ForwardingTargetOptions Options)
{
    /// <summary>Builds a target from a <c>telemetry_exports</c> row; null when <paramref name="configJson"/> is the JSON <c>null</c>.</summary>
    public static ManagedForwardingTarget? FromRow(Guid id, string name, string configJson)
    {
        var config = JsonSerializer.Deserialize(configJson, ForwardingConfigJsonContext.Default.ForwardingConfigJson);
        return config is null
            ? null
            : new ManagedForwardingTarget(id, new ForwardingTargetOptions
            {
                Name = name,
                Endpoint = config.Endpoint ?? "",
                Headers = config.Headers ?? [],
                Signals = config.Signals ?? [],
                Services = config.Services ?? [],
                IngestKeyIds = config.IngestKeyIds ?? [],
                Gzip = config.Gzip ?? true,
            });
    }
}

public interface IForwardingTargetStore
{
    Task<IReadOnlyList<ManagedForwardingTarget>> GetEnabledAsync(CancellationToken cancellationToken);
}

/// <summary>The <c>ConfigJson</c> of a forwarding row - mirrors Flare.Api's <c>ForwardingConfig</c>.</summary>
internal sealed record ForwardingConfigJson
{
    public string? Endpoint { get; init; }

    // Nullable with the defaults applied in FromRow: the source-generated deserializer leaves an absent
    // property null rather than keeping its initializer.
    public Dictionary<string, string>? Headers { get; init; }

    public List<ForwardingSignal>? Signals { get; init; }

    public List<string>? Services { get; init; }

    public List<Guid>? IngestKeyIds { get; init; }

    public bool? Gzip { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ForwardingConfigJson))]
internal sealed partial class ForwardingConfigJsonContext : JsonSerializerContext;

/// <summary>
/// Read-only view of <c>telemetry_exports</c> (Kind 0): latest version per <c>Id</c> (not <c>FINAL</c> - ADR-0074),
/// enabled and not deleted. Flare.Api owns writes.
/// </summary>
public sealed class ClickHouseForwardingTargetStore(IClickHouseClient client) : IForwardingTargetStore
{
    private const string Sql = """
        SELECT Id, Name, ConfigJson
        FROM (
            SELECT Id, Name, ConfigJson, Enabled, IsDeleted, Kind
            FROM telemetry_exports
            WHERE Kind = 0
            ORDER BY UpdatedAt DESC
            LIMIT 1 BY Id
        )
        WHERE IsDeleted = 0 AND Enabled = 1
        ORDER BY Name
        """;

    public async Task<IReadOnlyList<ManagedForwardingTarget>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        await using var reader = await client.ExecuteReaderAsync(Sql, null, SafetyOptions(), cancellationToken);
        var targets = new List<ManagedForwardingTarget>();
        while (reader.Read())
        {
            if (ManagedForwardingTarget.FromRow(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)) is { } target)
            {
                targets.Add(target);
            }
        }

        return targets;
    }

    private static QueryOptions SafetyOptions() => new()
    {
        CustomSettings = new Dictionary<string, object>
        {
            ["max_execution_time"] = 10,
            ["timeout_before_checking_execution_speed"] = 0,
            ["max_rows_to_read"] = 1_000_000_000,
            ["max_result_rows"] = 10_000,
            ["result_overflow_mode"] = "break",
        },
    };
}
