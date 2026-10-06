using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the synthetic monitor DTOs
/// <see cref="Endpoints.SyntheticMonitorEndpoints"/> serves - same convention as <see cref="OnCallRotationsJsonContext"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SyntheticMonitorRequest))]
[JsonSerializable(typeof(SyntheticMonitor))]
[JsonSerializable(typeof(SyntheticMonitorListResponse))]
[JsonSerializable(typeof(SyntheticMonitorStatus))]
[JsonSerializable(typeof(SyntheticLocationStatus))]
public sealed partial class SyntheticMonitorsJsonContext : JsonSerializerContext;
