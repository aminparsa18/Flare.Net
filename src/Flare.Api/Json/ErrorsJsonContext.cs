using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the request/response DTOs
/// <see cref="Endpoints.ExceptionEndpoints"/> serves - same camelCase/string-enum conventions
/// as <see cref="SpansJsonContext"/>. Nested types reachable from the roots below
/// (<see cref="ExceptionFilter"/>, <see cref="ExceptionGroup"/>, <see cref="ExceptionOccurrence"/>)
/// are picked up transitively.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ExceptionGroupsRequest))]
[JsonSerializable(typeof(ExceptionGroupsResponse))]
[JsonSerializable(typeof(ExceptionOccurrencesRequest))]
[JsonSerializable(typeof(ExceptionOccurrencesResponse))]
public sealed partial class ErrorsJsonContext : JsonSerializerContext;
