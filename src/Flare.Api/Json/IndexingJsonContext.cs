using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for <see cref="Endpoints.IndexingEndpoints"/>'s
/// request/response DTOs - same camelCase convention as <see cref="IngestionJsonContext"/>.
/// String enums for the promoted-attribute DTOs' <see cref="AttributeBag"/>, same as
/// <see cref="LogsJsonContext"/> serializes it inside a <see cref="LogFilter"/>.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(IndexingStatsResponse))]
[JsonSerializable(typeof(ClusterStatusResponse))]
[JsonSerializable(typeof(PromotedAttributesResponse))]
[JsonSerializable(typeof(PromoteAttributeRequest))]
public sealed partial class IndexingJsonContext : JsonSerializerContext;
