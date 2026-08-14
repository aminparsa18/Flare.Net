using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="ResourceGraphSnapshot"/> - the payload behind both
/// <c>GET /api/resources/snapshot</c> and <c>GET /api/resources/watch</c>. Same
/// camelCase-properties/string-enum conventions as <see cref="LogsJsonContext"/>/
/// <see cref="LogTailJsonContext"/>; kept as its own context since it's a distinct feature
/// area, not request/response DTOs for an existing one.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ResourceGraphSnapshot))]
public sealed partial class ResourceGraphJsonContext : JsonSerializerContext;
