using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.VersionEndpoints"/>'s response - same camelCase convention as
/// <see cref="PodsJsonContext"/>. JSON only: the dashboard fetches this once per page load,
/// not worth a MemoryPack decoder.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(VersionInfoResponse))]
public sealed partial class VersionJsonContext : JsonSerializerContext;
