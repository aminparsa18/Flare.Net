using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the request/response DTOs
/// <see cref="Endpoints.TraceFunnelEndpoints"/> serves - same camelCase/string-enum conventions
/// as <see cref="MessagingJsonContext"/>. Row types are picked up transitively.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(TraceFunnelRequest))]
[JsonSerializable(typeof(TraceFunnelResponse))]
[JsonSerializable(typeof(TraceFunnelTracesRequest))]
[JsonSerializable(typeof(TraceFunnelTracesResponse))]
public sealed partial class TraceFunnelJsonContext : JsonSerializerContext;
