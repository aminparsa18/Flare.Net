using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated contract for <see cref="Endpoints.UsageEndpoints"/> - same camelCase convention as <see cref="IndexingJsonContext"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UsageResponse))]
public sealed partial class UsageJsonContext : JsonSerializerContext;
