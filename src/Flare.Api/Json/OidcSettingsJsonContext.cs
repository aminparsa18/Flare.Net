using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.OidcSettingsEndpoints"/>'s DTOs - camelCase, string enums, same
/// convention as <see cref="LdapSettingsJsonContext"/> (this DTO pair also carries a
/// <c>UserRole</c> enum, unlike <see cref="EntraSettingsJsonContext"/>'s).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(OidcSettingsDto))]
[JsonSerializable(typeof(SaveOidcSettingsRequest))]
public sealed partial class OidcSettingsJsonContext : JsonSerializerContext;
