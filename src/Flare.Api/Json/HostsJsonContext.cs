using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.HostInventoryEndpoints"/>'s request/response DTOs - same camelCase
/// convention as <see cref="ServicesJsonContext"/>. <see cref="HostSummary"/>/
/// <see cref="HostMetricsPoint"/> are picked up transitively.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HostListRequest))]
[JsonSerializable(typeof(HostListResponse))]
[JsonSerializable(typeof(HostMetricsRequest))]
[JsonSerializable(typeof(HostMetricsResponse))]
public sealed partial class HostsJsonContext : JsonSerializerContext;
