using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.PodMetricsEndpoints"/>'s request/response DTOs - same camelCase
/// convention as <see cref="HostsJsonContext"/>.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PodMetricsRequest))]
[JsonSerializable(typeof(PodMetricsResponse))]
public sealed partial class PodsJsonContext : JsonSerializerContext;
