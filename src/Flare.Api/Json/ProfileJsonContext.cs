using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the DTOs <see cref="Endpoints.ProfileEndpoints"/>
/// serves - same conventions as <see cref="ExternalApiJsonContext"/>. Row types are picked up transitively.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProfileTypesRequest))]
[JsonSerializable(typeof(ProfileTypesResponse))]
[JsonSerializable(typeof(FlameGraphRequest))]
[JsonSerializable(typeof(FlameGraphResponse))]
public sealed partial class ProfileJsonContext : JsonSerializerContext;
