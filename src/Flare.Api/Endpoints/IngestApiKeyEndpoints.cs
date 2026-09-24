using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Identity.IngestKeys;

namespace Flare.Api.Endpoints;

/// <summary>
/// Manages OTLP ingest API keys, under <c>/api/ingest-keys</c> - Admin-only (see
/// <c>Program.cs</c>'s <c>RequireAdmin</c> policy group). These authenticate the
/// telemetry-emitting apps/collectors calling <c>Flare.Ingest</c>'s OTLP receiver, not
/// dashboard users - see <see cref="IIngestApiKeyStore"/>'s remarks for why they're
/// deliberately not tied to a <see cref="Identity.Users.User"/> row.
/// </summary>
public static class IngestApiKeyEndpoints
{
    public static IEndpointRouteBuilder MapIngestApiKeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/ingest-keys", HandleCreateAsync);
        endpoints.MapGet("/api/ingest-keys", HandleListAsync);
        endpoints.MapDelete("/api/ingest-keys/{id:guid}", HandleRevokeAsync);
        endpoints.MapPut("/api/ingest-keys/{id:guid}/limits", HandleUpdateLimitsAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, IIngestApiKeyStore keys, CancellationToken cancellationToken)
    {
        CreateIngestApiKeyRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, IngestApiKeysJsonContext.Default.CreateIngestApiKeyRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.Problem("Name is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var (key, rawKey) = await keys.CreateAsync(request.Name, cancellationToken);
        var response = new CreateIngestApiKeyResponse { Key = ToDto(key, default), RawKey = rawKey };
        return ApiSerialization.Write(http, response, IngestApiKeysJsonContext.Default.CreateIngestApiKeyResponse, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(
        HttpContext http,
        IIngestApiKeyStore keys,
        IIngestApiKeyUsageQueryService usageQuery,
        CancellationToken cancellationToken)
    {
        var list = await keys.ListAsync(cancellationToken);

        // Revoked keys can't ingest, so there's nothing current to read for them.
        var usage = await usageQuery.GetUsageAsync(list.Where(k => k.IsActive).Select(k => k.Id).ToList(), cancellationToken);

        var response = new IngestApiKeyListResponse
        {
            Keys = list.Select(k => ToDto(k, usage.GetValueOrDefault(k.Id))).ToList(),
        };
        return ApiSerialization.Write(http, response, IngestApiKeysJsonContext.Default.IngestApiKeyListResponse);
    }

    /// <summary>Takes effect on <c>Flare.Ingest</c> within its key cache's refresh interval
    /// (30s), same as a revocation - see <c>IngestApiKeyCache</c>'s remarks.</summary>
    private static async Task<IResult> HandleUpdateLimitsAsync(Guid id, HttpContext http, IIngestApiKeyStore keys, CancellationToken cancellationToken)
    {
        UpdateIngestApiKeyLimitsRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, IngestApiKeysJsonContext.Default.UpdateIngestApiKeyLimitsRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (ValidateLimits(request) is { } error)
        {
            return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        var limits = new IngestApiKeyLimits(
            request.LimitsEnabled,
            request.MaxEventsPerMinute,
            request.MaxBytesPerMinute,
            request.MaxEventsPerDay,
            request.MaxBytesPerDay);

        return await keys.UpdateLimitsAsync(id, limits, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }

    /// <summary>Null means "no cap"; a set cap must be positive - a zero cap would just be a
    /// disguised revoke, and revoking already exists.</summary>
    public static string? ValidateLimits(UpdateIngestApiKeyLimitsRequest request)
    {
        (string Name, long? Value)[] caps =
        [
            (nameof(request.MaxEventsPerMinute), request.MaxEventsPerMinute),
            (nameof(request.MaxBytesPerMinute), request.MaxBytesPerMinute),
            (nameof(request.MaxEventsPerDay), request.MaxEventsPerDay),
            (nameof(request.MaxBytesPerDay), request.MaxBytesPerDay),
        ];

        foreach (var (name, value) in caps)
        {
            if (value is <= 0)
            {
                return $"{name} must be greater than zero, or omitted for no cap.";
            }
        }

        return null;
    }

    private static async Task<IResult> HandleRevokeAsync(Guid id, IIngestApiKeyStore keys, CancellationToken cancellationToken)
    {
        await keys.RevokeAsync(id, cancellationToken);
        return Results.NoContent();
    }

    private static IngestApiKeyDto ToDto(IngestApiKey key, IngestApiKeyUsage usage) => new()
    {
        Id = key.Id,
        Name = key.Name,
        CreatedAt = key.CreatedAt,
        RevokedAt = key.RevokedAt,
        IsActive = key.IsActive,
        LimitsEnabled = key.Limits.Enabled,
        MaxEventsPerMinute = key.Limits.MaxEventsPerMinute,
        MaxBytesPerMinute = key.Limits.MaxBytesPerMinute,
        MaxEventsPerDay = key.Limits.MaxEventsPerDay,
        MaxBytesPerDay = key.Limits.MaxBytesPerDay,
        EventsThisMinute = usage.EventsThisMinute,
        BytesThisMinute = usage.BytesThisMinute,
        EventsToday = usage.EventsToday,
        BytesToday = usage.BytesToday,
    };
}
