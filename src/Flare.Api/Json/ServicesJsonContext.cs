using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.ServicesEndpoints"/>'s request/response DTOs - same camelCase
/// convention as <see cref="SpansJsonContext"/>. <see cref="ServiceMetrics"/> (reachable
/// from <see cref="ServiceOverviewResponse.Services"/>), <see cref="ServiceDependencyNode"/>/
/// <see cref="ServiceDependencyEdge"/> (reachable from <see cref="ServiceDependencyGraphResponse"/>),
/// <see cref="ExternalCallGroup"/>/<see cref="DatabaseCallGroup"/> (reachable from
/// <see cref="ServiceCallBreakdownResponse"/>), and <see cref="ResourceAttributeFilter"/>
/// (reachable from every request type's <c>ResourceAttributes</c> member) are picked up
/// transitively.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ServiceOverviewRequest))]
[JsonSerializable(typeof(ServiceOverviewResponse))]
[JsonSerializable(typeof(ServiceDependencyRequest))]
[JsonSerializable(typeof(ServiceDependencyGraphResponse))]
[JsonSerializable(typeof(ServiceCallBreakdownRequest))]
[JsonSerializable(typeof(ServiceCallBreakdownResponse))]
public sealed partial class ServicesJsonContext : JsonSerializerContext;
