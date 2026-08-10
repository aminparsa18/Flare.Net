using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the saved-view DTOs
/// <see cref="Endpoints.SavedViewEndpoints"/> serves - camelCase, string enums, same
/// convention as <see cref="AlertsJsonContext"/>. <see cref="System.Text.Json.JsonElement"/>
/// (the opaque <see cref="SavedView.State"/>/<see cref="SavedViewRequest.State"/> payload)
/// is a built-in System.Text.Json type and round-trips through a source-gen context
/// without its own <see cref="JsonSerializableAttribute"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SavedViewRequest))]
[JsonSerializable(typeof(SavedView))]
[JsonSerializable(typeof(SavedViewListResponse))]
public sealed partial class SavedViewsJsonContext : JsonSerializerContext;
