using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.UserEndpoints"/>'s DTOs - camelCase, string enums (matches
/// <see cref="AuthJsonContext"/>, since <see cref="Identity.Users.UserRole"/> is the same
/// enum both serialize).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(UserListResponse))]
[JsonSerializable(typeof(UserSummaryDto))]
[JsonSerializable(typeof(SetUserRoleRequest))]
[JsonSerializable(typeof(SetUserDisabledRequest))]
public sealed partial class UsersJsonContext : JsonSerializerContext;
