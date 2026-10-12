using System.Text.Json;
using Flare.Api.Auditing;
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
        endpoints.MapGet("/api/audit-events/export", HandleExportAsync);
        endpoints.MapGet("/api/audit-events/verify", HandleVerifyAsync);
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
                Changes = ParseChanges(e.Changes),
                PrevHash = e.PrevHash,
                Hash = e.Hash,
            }).ToList(),
            NextBefore = rows.Count > take ? page[^1].Id : null,
        };
        return Results.Json(response, AuditJsonContext.Default.AuditEventListResponse);
    }

    // Streams every matching event (newest first) in keyset pages, so memory stays flat however
    // large the retained trail is. Formats: csv (spreadsheets), json (array), ndjson (SIEM ingest).
    private static async Task<IResult> HandleExportAsync(
        HttpContext http,
        IAuditEventStore store,
        TimeProvider timeProvider,
        string? format,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? actorId,
        string? resourceType,
        string? action,
        CancellationToken cancellationToken)
    {
        var exportFormat = AuditExportFormatter.Parse(format);
        if (exportFormat is null)
        {
            return Results.BadRequest(new { error = "format must be csv, json or ndjson." });
        }

        var filter = new AuditEventFilter { From = from, To = to, ActorId = actorId, ResourceType = resourceType, Action = action };
        var fileName = $"flare-audit-{timeProvider.GetUtcNow():yyyyMMddHHmmss}.{AuditExportFormatter.Extension(exportFormat.Value)}";

        http.Response.ContentType = AuditExportFormatter.ContentType(exportFormat.Value);
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

        await using var writer = new StreamWriter(http.Response.Body, new System.Text.UTF8Encoding(false), leaveOpen: true);
        await AuditExportFormatter.WriteAsync(
            writer, exportFormat.Value, PageAsync(store, filter, cancellationToken), cancellationToken);
        await writer.FlushAsync(cancellationToken);
        return Results.Empty;
    }

    private static async IAsyncEnumerable<AuditEvent> PageAsync(
        IAuditEventStore store,
        AuditEventFilter filter,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        long? before = null;
        while (true)
        {
            var rows = await store.QueryAsync(filter, before, pageSize, cancellationToken);
            foreach (var row in rows)
            {
                yield return row;
            }

            if (rows.Count < pageSize)
            {
                yield break;
            }

            before = rows[^1].Id;
        }
    }

    private static async Task<IResult> HandleVerifyAsync(IAuditEventStore store, CancellationToken cancellationToken)
    {
        var result = await store.VerifyChainAsync(cancellationToken);
        return Results.Json(
            new AuditChainVerificationDto
            {
                Valid = result.Valid,
                ChainedEvents = result.ChainedEvents,
                LegacyEvents = result.LegacyEvents,
                FirstBrokenId = result.FirstBrokenId,
                Reason = result.Reason,
                HeadHash = result.HeadHash,
            },
            AuditJsonContext.Default.AuditChainVerificationDto);
    }

    private static IReadOnlyList<AuditFieldChangeDto> ParseChanges(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(json, AuditJsonContext.Default.ListAuditFieldChangeDto) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
