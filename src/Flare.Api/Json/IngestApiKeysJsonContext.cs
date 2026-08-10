using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.IngestApiKeyEndpoints"/>'s DTOs - camelCase, same convention as
/// <see cref="AuthJsonContext"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CreateIngestApiKeyRequest))]
[JsonSerializable(typeof(CreateIngestApiKeyResponse))]
[JsonSerializable(typeof(IngestApiKeyListResponse))]
public sealed partial class IngestApiKeysJsonContext : JsonSerializerContext;
