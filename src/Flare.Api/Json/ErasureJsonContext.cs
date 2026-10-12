using System.Text.Json.Serialization;
using Flare.Api.Erasure;

namespace Flare.Api.Json;

/// <summary>Source-generated JSON contract for <see cref="Endpoints.ErasureEndpoints"/> and the counts <see cref="ErasureService"/> persists - camelCase, same convention as <see cref="RetentionJsonContext"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ErasureRequest))]
[JsonSerializable(typeof(ErasurePreviewResponse))]
[JsonSerializable(typeof(StartErasureResponse))]
[JsonSerializable(typeof(ErasureOperation))]
[JsonSerializable(typeof(ErasureListResponse))]
[JsonSerializable(typeof(Dictionary<string, long>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, long>))]
public sealed partial class ErasureJsonContext : JsonSerializerContext;
