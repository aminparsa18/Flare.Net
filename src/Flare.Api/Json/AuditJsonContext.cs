using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>JSON-only (admin-only, low-volume page) camelCase contract for <c>GET /api/audit-events</c>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AuditEventListResponse))]
public sealed partial class AuditJsonContext : JsonSerializerContext;
