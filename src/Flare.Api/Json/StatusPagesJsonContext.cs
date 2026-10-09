using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the status page DTOs
/// <see cref="Endpoints.StatusPageEndpoints"/> serves - same convention as <see cref="AlertTemplatesJsonContext"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(StatusPageRequest))]
[JsonSerializable(typeof(StatusPage))]
[JsonSerializable(typeof(StatusPageListResponse))]
[JsonSerializable(typeof(PublicStatusPage))]
[JsonSerializable(typeof(IReadOnlyList<StatusPageComponent>))]
[JsonSerializable(typeof(StatusIncidentRequest))]
[JsonSerializable(typeof(StatusIncidentUpdateRequest))]
[JsonSerializable(typeof(StatusIncident))]
[JsonSerializable(typeof(StatusIncidentListResponse))]
[JsonSerializable(typeof(IReadOnlyList<StatusIncidentUpdate>))]
[JsonSerializable(typeof(StatusSubscribeRequest))]
[JsonSerializable(typeof(StatusSubscriptionTokenRequest))]
[JsonSerializable(typeof(StatusSubscriptionInfo))]
[JsonSerializable(typeof(StatusSubscriberListResponse))]
public sealed partial class StatusPagesJsonContext : JsonSerializerContext;
