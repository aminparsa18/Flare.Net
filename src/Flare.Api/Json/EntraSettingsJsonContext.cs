using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.EntraSettingsEndpoints"/>'s DTOs - camelCase, same convention as
/// <see cref="UsersJsonContext"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EntraSettingsDto))]
[JsonSerializable(typeof(SaveEntraSettingsRequest))]
public sealed partial class EntraSettingsJsonContext : JsonSerializerContext;
