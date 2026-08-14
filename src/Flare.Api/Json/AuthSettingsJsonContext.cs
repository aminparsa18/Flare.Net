using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.AuthSettingsEndpoints"/>'s DTO - camelCase, same convention as
/// <see cref="EntraSettingsJsonContext"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AuthSettingsDto))]
public sealed partial class AuthSettingsJsonContext : JsonSerializerContext;
