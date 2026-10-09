using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated <see cref="System.Text.Json"/> contract for <see cref="Endpoints.AppSessionEndpoints"/>; same conventions as <see cref="ExternalApiJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSessionsRequest))]
[JsonSerializable(typeof(AppSessionsResponse))]
public sealed partial class AppSessionJsonContext : JsonSerializerContext;
