using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated JSON contract for <see cref="Endpoints.SourceLinkEndpoints"/> - camelCase, string enums, same convention as <see cref="MetricAttributeRulesJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SourceLinkDto))]
[JsonSerializable(typeof(SourceLinkListResponse))]
public sealed partial class SourceLinksJsonContext : JsonSerializerContext;
