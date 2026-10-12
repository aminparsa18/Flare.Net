using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated JSON contract for <see cref="Endpoints.SourceMapEndpoints"/> - camelCase, same convention as <see cref="SourceLinksJsonContext"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SourceMapDto))]
[JsonSerializable(typeof(SourceMapListResponse))]
public sealed partial class SourceMapsJsonContext : JsonSerializerContext;
