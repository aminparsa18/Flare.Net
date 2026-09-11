using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.PersonalAccessTokenEndpoints"/>'s DTOs - camelCase, same
/// convention as <see cref="IngestApiKeysJsonContext"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CreateAccessTokenRequest))]
[JsonSerializable(typeof(CreateAccessTokenResponse))]
[JsonSerializable(typeof(AccessTokenListResponse))]
public sealed partial class PersonalAccessTokensJsonContext : JsonSerializerContext;
