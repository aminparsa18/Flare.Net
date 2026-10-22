using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the DTOs <see cref="Endpoints.ProfileEndpoints"/>
/// serves - same conventions as <see cref="ExternalApiJsonContext"/>. Row types are picked up transitively.
/// </summary>
/// <remarks>
/// <c>MaxDepth</c> is raised well past System.Text.Json's default of 64: a flame graph nests an object
/// and an array per frame, so a real stack of ~35 frames already overflowed the default and the response
/// died mid-body after a 200 (seen with the eBPF profiler). Stacks are capped to
/// <see cref="Query.FlameGraphBuilder.MaxFrames"/> frames, which stays under this.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    MaxDepth = 2 * (Query.FlameGraphBuilder.MaxFrames + 2))]
[JsonSerializable(typeof(ProfileTypesRequest))]
[JsonSerializable(typeof(ProfileTypesResponse))]
[JsonSerializable(typeof(FlameGraphRequest))]
[JsonSerializable(typeof(FlameGraphResponse))]
public sealed partial class ProfileJsonContext : JsonSerializerContext;
