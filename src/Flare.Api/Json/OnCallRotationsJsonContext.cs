using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the on-call rotation DTOs
/// <see cref="Endpoints.OnCallRotationEndpoints"/> serves - same convention as <see cref="MaintenanceWindowsJsonContext"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(OnCallRotationRequest))]
[JsonSerializable(typeof(OnCallOverride[]))]
[JsonSerializable(typeof(OnCallCoverage))]
[JsonSerializable(typeof(OnCallRotation))]
[JsonSerializable(typeof(OnCallRotationStatus))]
[JsonSerializable(typeof(OnCallRotationListResponse))]
public sealed partial class OnCallRotationsJsonContext : JsonSerializerContext;
