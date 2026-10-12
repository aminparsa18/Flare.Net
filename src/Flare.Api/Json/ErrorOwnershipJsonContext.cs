using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated <see cref="System.Text.Json"/> contract for <see cref="Endpoints.ErrorOwnershipEndpoints"/> - same camelCase/string-enum convention as <see cref="ErrorIssuesJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ErrorOwnershipRulesDocument))]
public sealed partial class ErrorOwnershipJsonContext : JsonSerializerContext;
