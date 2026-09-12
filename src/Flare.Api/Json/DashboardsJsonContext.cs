using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the dashboard DTOs
/// <see cref="Endpoints.DashboardEndpoints"/> serves - camelCase, string enums, same
/// convention as <see cref="SavedViewsJsonContext"/>. <see cref="System.Text.Json.JsonElement"/>
/// (the opaque <see cref="Dashboard.LayoutJson"/>/<see cref="DashboardRequest.LayoutJson"/>
/// payload) is a built-in System.Text.Json type and round-trips through a source-gen
/// context without its own <see cref="JsonSerializableAttribute"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DashboardRequest))]
[JsonSerializable(typeof(Dashboard))]
[JsonSerializable(typeof(DashboardListResponse))]
public sealed partial class DashboardsJsonContext : JsonSerializerContext;
