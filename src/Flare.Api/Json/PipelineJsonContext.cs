using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.PipelineEndpoints"/>'s response - same camelCase/string-enum
/// conventions as <see cref="IngestionJsonContext"/>. Nested types are picked up
/// transitively.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PipelineStatsResponse))]
public sealed partial class PipelineJsonContext : JsonSerializerContext;
