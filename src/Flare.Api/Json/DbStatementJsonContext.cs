using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>Source-generated JSON contract for <see cref="Endpoints.DbStatementEndpoints"/> - same conventions as <see cref="NPlusOneJsonContext"/>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DbStatementRequest))]
[JsonSerializable(typeof(DbStatementResponse))]
public sealed partial class DbStatementJsonContext : JsonSerializerContext;
