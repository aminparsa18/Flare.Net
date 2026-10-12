using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the alert-template DTOs
/// <see cref="Endpoints.AlertTemplateEndpoints"/> serves - same convention as
/// <see cref="MaintenanceWindowsJsonContext"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AlertTemplateRequest))]
[JsonSerializable(typeof(AlertTemplate))]
[JsonSerializable(typeof(IReadOnlyList<AlertTemplate>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
public sealed partial class AlertTemplatesJsonContext : JsonSerializerContext;
