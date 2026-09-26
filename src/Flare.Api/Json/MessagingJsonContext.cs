using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the request/response DTOs
/// <see cref="Endpoints.MessagingEndpoints"/> serves - same camelCase/string-enum conventions
/// as <see cref="ErrorsJsonContext"/>. Row types are picked up transitively.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(MessagingDestinationsRequest))]
[JsonSerializable(typeof(MessagingDestinationsResponse))]
[JsonSerializable(typeof(MessagingDestinationDetailRequest))]
[JsonSerializable(typeof(MessagingDestinationDetailResponse))]
public sealed partial class MessagingJsonContext : JsonSerializerContext;
