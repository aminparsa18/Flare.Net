using System.Text.Json.Serialization;
using Flare.Api.Retention;

namespace Flare.Api.Json;

/// <summary>Source-generated JSON contract for <see cref="Endpoints.RetentionEndpoints"/> - camelCase, same convention as <see cref="LogMetricsJsonContext"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RetentionResponse))]
[JsonSerializable(typeof(SetRetentionRequest))]
[JsonSerializable(typeof(SetRetentionResponse))]
public sealed partial class RetentionJsonContext : JsonSerializerContext;
