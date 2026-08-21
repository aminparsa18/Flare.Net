using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the request/response
/// DTOs <see cref="Endpoints.LogsEndpoints"/> serves - camelCase on the wire (this is a
/// public HTTP API, unlike <c>Flare.Ingest</c>'s internal Redis wire format), enums as
/// strings for readability. Nested types reachable from the roots below
/// (<see cref="LogFilter"/>, <see cref="AttributeFilter"/>, <see cref="LogEventDto"/>,
/// <see cref="LogAggregateBucket"/>) are picked up transitively by the source generator -
/// no need to list them individually.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(LogSearchRequest))]
[JsonSerializable(typeof(LogSearchResponse))]
[JsonSerializable(typeof(LogAggregateRequest))]
[JsonSerializable(typeof(LogAggregateResponse))]
[JsonSerializable(typeof(LogPatternRequest))]
[JsonSerializable(typeof(LogPatternResponse))]
[JsonSerializable(typeof(LogAttributeKeysRequest))]
[JsonSerializable(typeof(LogAttributeKeysResponse))]
[JsonSerializable(typeof(LogValueDistributionRequest))]
[JsonSerializable(typeof(LogValueDistributionResponse))]
public sealed partial class LogsJsonContext : JsonSerializerContext;
