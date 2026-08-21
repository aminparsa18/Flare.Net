using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="HostStatsSnapshot"/> - the payload behind both
/// <c>GET /api/resources/host/snapshot</c> and <c>GET /api/resources/host/watch</c>. Same
/// camelCase-properties convention as <see cref="ResourceGraphJsonContext"/>; kept as its
/// own context since it's a distinct feature area (no enums here, so no
/// <c>UseStringEnumConverter</c> needed).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HostStatsSnapshot))]
public sealed partial class HostStatsJsonContext : JsonSerializerContext;
