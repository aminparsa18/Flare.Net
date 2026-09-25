using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the maintenance-window DTOs
/// <see cref="Endpoints.MaintenanceWindowEndpoints"/> serves - same camelCase/string-enum
/// convention and one-context-per-endpoint-file scoping as <see cref="NotificationChannelsJsonContext"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(MaintenanceWindowRequest))]
[JsonSerializable(typeof(MaintenanceWindow))]
[JsonSerializable(typeof(MaintenanceWindowListResponse))]
public sealed partial class MaintenanceWindowsJsonContext : JsonSerializerContext;
