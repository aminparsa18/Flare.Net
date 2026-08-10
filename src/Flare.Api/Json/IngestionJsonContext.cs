using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for <see cref="Endpoints.IngestionEndpoints"/>'s
/// response - same camelCase/string-enum conventions as <see cref="MetricsJsonContext"/>.
/// Nested types (<see cref="IngestionBucketPoint"/>, <see cref="IngestionStatsTotals"/>,
/// <see cref="IngestionErrorEntryDto"/>, <see cref="IngestionSignal"/>, <see cref="IngestionProtocol"/>)
/// are picked up transitively. Deliberately not shared with <see cref="Query.IngestionErrorWireJsonContext"/> -
/// that one parses the plain-PascalCase blobs Flare.Ingest actually wrote to Redis, an
/// unrelated wire format this API's own JSON response has no reason to match.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(IngestionStatsResponse))]
public sealed partial class IngestionJsonContext : JsonSerializerContext;
