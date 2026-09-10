using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.ServicesEndpoints"/>'s response DTOs - same camelCase convention
/// as <see cref="SpansJsonContext"/>. <see cref="ServiceMetrics"/> (reachable from
/// <see cref="ServiceOverviewResponse.Services"/>), <see cref="ServiceDependencyNode"/>/
/// <see cref="ServiceDependencyEdge"/> (reachable from <see cref="ServiceDependencyGraphResponse"/>),
/// and <see cref="ExternalCallGroup"/>/<see cref="DatabaseCallGroup"/> (reachable from
/// <see cref="ServiceCallBreakdownResponse"/>) are picked up transitively.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ServiceOverviewResponse))]
[JsonSerializable(typeof(ServiceDependencyGraphResponse))]
[JsonSerializable(typeof(ServiceCallBreakdownResponse))]
public sealed partial class ServicesJsonContext : JsonSerializerContext;
