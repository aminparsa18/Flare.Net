using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.ProxyAuthSettingsEndpoints"/>'s DTOs - camelCase, string enums,
/// same convention as <see cref="LdapSettingsJsonContext"/>/<see cref="OidcSettingsJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProxyAuthSettingsDto))]
[JsonSerializable(typeof(SaveProxyAuthSettingsRequest))]
public sealed partial class ProxyAuthJsonContext : JsonSerializerContext;
