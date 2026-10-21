using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated <see cref="System.Text.Json"/> contract for <see cref="Endpoints.WebReplayEndpoints"/>; same conventions as <see cref="AppSessionJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSessionTimelineRequest))]
[JsonSerializable(typeof(WebReplayResponse))]
[JsonSerializable(typeof(WebReplayChunkRequest))]
public sealed partial class WebReplayJsonContext : JsonSerializerContext;
