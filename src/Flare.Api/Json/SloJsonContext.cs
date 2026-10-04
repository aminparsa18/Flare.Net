using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the SLO DTOs
/// <see cref="Endpoints.SloEndpoints"/> serves, and for <see cref="SloBurnRateCondition"/>'s
/// <c>SloConditionJson</c> round-trip in <see cref="Query.AlertQueryService"/> - same
/// camelCase/string-enum convention as <see cref="MaintenanceWindowsJsonContext"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SloRequest))]
[JsonSerializable(typeof(Slo))]
[JsonSerializable(typeof(SloListResponse))]
[JsonSerializable(typeof(SloStatus))]
public sealed partial class SloJsonContext : JsonSerializerContext;
