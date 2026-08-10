using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the request/response
/// DTOs <see cref="Endpoints.SpanEndpoints"/> serves - same camelCase/string-enum
/// conventions as <see cref="LogsJsonContext"/>. Nested types reachable from the roots
/// below (<see cref="SpanFilter"/>, <see cref="SpanAttributeFilter"/>, <see cref="SpanDto"/>,
/// <see cref="SpanEventDto"/>) are picked up transitively.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SpanSearchRequest))]
[JsonSerializable(typeof(SpanSearchResponse))]
[JsonSerializable(typeof(TraceDto))]
public sealed partial class SpansJsonContext : JsonSerializerContext;
