using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for <see cref="Endpoints.IndexingEndpoints"/>'s
/// response - same camelCase convention as <see cref="IngestionJsonContext"/>. No string
/// enums here (no enum-typed field in this response).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IndexingStatsResponse))]
public sealed partial class IndexingJsonContext : JsonSerializerContext;
