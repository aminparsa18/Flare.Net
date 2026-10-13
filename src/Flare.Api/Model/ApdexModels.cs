using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// One service's configured Apdex threshold override - see
/// docs-internal/adr/0032-apdex-score-per-service.md and
/// <see cref="Flare.Identity.Apdex.IApdexThresholdStore"/>. No
/// <see cref="DateTimeOffset"/>/<see cref="System.Text.Json.JsonElement"/> member, so this
/// carries <c>[GenerateTypeScript]</c> - see <c>ServiceOverviewModels.cs</c>'s
/// <see cref="ServiceMetrics"/> for the same reasoning.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record ApdexThresholdDto
{
    public required string ServiceName { get; init; }

    public required int ThresholdMs { get; init; }
}

/// <summary>
/// Response body for <c>GET /api/services/apdex-thresholds</c>: the system default plus
/// every service with a configured override (services with no override aren't listed -
/// see <see cref="Flare.Identity.Apdex.IApdexThresholdStore.GetAllAsync"/>). Deliberately
/// hand-written on the MemoryPack TS side (not <c>[GenerateTypeScript]</c>) - an
/// <c>IReadOnlyList&lt;ApdexThresholdDto&gt;</c> member blocks the generator, same
/// precedent as <c>ServiceOverviewResponse</c>.
/// </summary>
[MemoryPackable]
public sealed partial record ApdexThresholdsResponse
{
    /// <summary>See <see cref="Query.ServiceApdexQueryBuilder.DefaultThresholdMs"/>.</summary>
    public required int DefaultThresholdMs { get; init; }

    public required IReadOnlyList<ApdexThresholdDto> Overrides { get; init; }
}

/// <summary>Request body for <c>PUT /api/services/apdex-thresholds/{serviceName}</c>.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record SetApdexThresholdRequest
{
    public required int ThresholdMs { get; init; }
}
