using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
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
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, IIngestApiKeyStore keys, CancellationToken cancellationToken)
    {
        CreateIngestApiKeyRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(http.Request.Body, IngestApiKeysJsonContext.Default.CreateIngestApiKeyRequest, cancellationToken);
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
        var response = new CreateIngestApiKeyResponse { Key = ToDto(key), RawKey = rawKey };
        return Results.Json(response, IngestApiKeysJsonContext.Default.CreateIngestApiKeyResponse, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(IIngestApiKeyStore keys, CancellationToken cancellationToken)
    {
        var list = await keys.ListAsync(cancellationToken);
        var response = new IngestApiKeyListResponse { Keys = list.Select(ToDto).ToList() };
        return Results.Json(response, IngestApiKeysJsonContext.Default.IngestApiKeyListResponse);
    }

    private static async Task<IResult> HandleRevokeAsync(Guid id, IIngestApiKeyStore keys, CancellationToken cancellationToken)
    {
        await keys.RevokeAsync(id, cancellationToken);
        return Results.NoContent();
    }

    private static IngestApiKeyDto ToDto(IngestApiKey key) => new()
    {
        Id = key.Id,
        Name = key.Name,
        CreatedAt = key.CreatedAt,
        RevokedAt = key.RevokedAt,
        IsActive = key.IsActive,
    };
}
