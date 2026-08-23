using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for <see cref="Endpoints.IndexingEndpoints"/>'s
/// responses - same camelCase convention as <see cref="IngestionJsonContext"/>. No string
/// enums here (no enum-typed field in either response).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IndexingStatsResponse))]
[JsonSerializable(typeof(ClusterStatusResponse))]
public sealed partial class IndexingJsonContext : JsonSerializerContext;
