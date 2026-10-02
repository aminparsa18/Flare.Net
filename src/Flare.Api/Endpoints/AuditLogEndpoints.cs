using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.Audit;

namespace Flare.Api.Endpoints;

/// <summary>
/// <c>GET /api/audit-events</c> - the audit trail (ADR-0079), Admin-only (mapped onto
/// <c>Program.cs</c>'s <c>adminRoutes</c>). Newest first, keyset-paged by <c>before</c>.
/// </summary>
public static class AuditLogEndpoints
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    public static IEndpointRouteBuilder MapAuditLogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/audit-events", HandleListAsync);
        return endpoints;
    }

    // Results.Json, not ApiSerialization.Write - not [MemoryPackable] (see VersionEndpoints).
    private static async Task<IResult> HandleListAsync(
        IAuditEventStore store,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? actorId,
        string? resourceType,
        string? action,
        long? before,
        int? limit,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var filter = new AuditEventFilter { From = from, To = to, ActorId = actorId, ResourceType = resourceType, Action = action };

        // One extra row tells us whether another page exists without a COUNT.
        var rows = await store.QueryAsync(filter, before, take + 1, cancellationToken);
        var page = rows.Take(take).ToList();

        var response = new AuditEventListResponse
        {
            Events = page.Select(e => new AuditEventDto
            {
                Id = e.Id,
                Timestamp = e.Timestamp,
                ActorId = e.ActorId,
                ActorName = e.ActorName,
                ActorKind = e.ActorKind,
                Action = e.Action,
                ResourceType = e.ResourceType,
                ResourceId = e.ResourceId,
                Route = e.Route,
                StatusCode = e.StatusCode,
                SourceIp = e.SourceIp,
            }).ToList(),
            NextBefore = rows.Count > take ? page[^1].Id : null,
        };
        return Results.Json(response, AuditJsonContext.Default.AuditEventListResponse);
    }
}
