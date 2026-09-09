using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.ServicesEndpoints"/>'s response DTO - same camelCase convention
/// as <see cref="SpansJsonContext"/>. <see cref="ServiceMetrics"/> (reachable from
/// <see cref="ServiceOverviewResponse.Services"/>) is picked up transitively.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ServiceOverviewResponse))]
public sealed partial class ServicesJsonContext : JsonSerializerContext;
