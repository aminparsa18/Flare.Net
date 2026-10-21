using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the check-in monitor DTOs
/// <see cref="Endpoints.CheckInMonitorEndpoints"/> serves - same convention as <see cref="SyntheticMonitorsJsonContext"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CheckInMonitorRequest))]
[JsonSerializable(typeof(CheckInMonitor))]
[JsonSerializable(typeof(CheckInMonitorListResponse))]
[JsonSerializable(typeof(CheckInRunListResponse))]
public sealed partial class CheckInMonitorsJsonContext : JsonSerializerContext;
