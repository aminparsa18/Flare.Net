using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.LdapSettingsEndpoints"/>'s DTOs - camelCase, string enums, same
/// convention as <see cref="AuthJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(LdapSettingsDto))]
[JsonSerializable(typeof(SaveLdapSettingsRequest))]
public sealed partial class LdapSettingsJsonContext : JsonSerializerContext;
