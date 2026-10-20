using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated <see cref="System.Text.Json"/> contract for <see cref="Endpoints.ErrorGroupingEndpoints"/> - same camelCase/string-enum convention as <see cref="ErrorOwnershipJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ErrorGroupingDocument))]
public sealed partial class ErrorGroupingJsonContext : JsonSerializerContext;
