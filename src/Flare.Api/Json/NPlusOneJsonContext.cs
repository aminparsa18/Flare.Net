using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated JSON contract for <see cref="Endpoints.NPlusOneEndpoints"/> - same conventions as <see cref="TraceFunnelJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(NPlusOneRequest))]
[JsonSerializable(typeof(NPlusOneResponse))]
public sealed partial class NPlusOneJsonContext : JsonSerializerContext;
