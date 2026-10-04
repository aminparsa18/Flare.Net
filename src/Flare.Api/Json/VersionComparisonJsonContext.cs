using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated JSON contract for the version-comparison endpoint - same conventions as <see cref="RuntimeHealthJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(VersionComparisonRequest))]
[JsonSerializable(typeof(VersionComparisonResponse))]
public sealed partial class VersionComparisonJsonContext : JsonSerializerContext;
