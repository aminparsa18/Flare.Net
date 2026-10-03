using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the request/response DTOs
/// <see cref="Endpoints.LlmEndpoints"/> serves - same camelCase/string-enum conventions as
/// <see cref="MessagingJsonContext"/>. Row types are picked up transitively.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(LlmModelsRequest))]
[JsonSerializable(typeof(LlmModelsResponse))]
public sealed partial class LlmJsonContext : JsonSerializerContext;
