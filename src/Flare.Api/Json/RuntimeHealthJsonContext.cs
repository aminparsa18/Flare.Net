using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated JSON contract for the runtime-health endpoint - same conventions as <see cref="NPlusOneJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(RuntimeHealthRequest))]
[JsonSerializable(typeof(RuntimeHealthResponse))]
public sealed partial class RuntimeHealthJsonContext : JsonSerializerContext;
