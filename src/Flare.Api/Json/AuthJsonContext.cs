using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.AuthEndpoints"/>'s DTOs - camelCase, string enums, same
/// convention as <see cref="SavedViewsJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(AuthUserDto))]
[JsonSerializable(typeof(BootstrapStatusResponse))]
public sealed partial class AuthJsonContext : JsonSerializerContext;
