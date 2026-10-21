using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// Browser session replay (ADR-0264): <c>POST /api/web-replay</c> lists a session's recorded chunks and
/// <c>POST /api/web-replay/chunk</c> returns one chunk's rrweb events as a bare JSON array. Both go through the caller's service scope.
/// </summary>
public static class WebReplayEndpoints
{
    public static IEndpointRouteBuilder MapWebReplayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/web-replay", HandleChunksAsync);
        endpoints.MapPost("/api/web-replay/chunk", HandleChunkAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleChunksAsync(
        HttpContext http,
        IWebReplayQueryService queryService,
        CancellationToken cancellationToken)
    {
        AppSessionTimelineRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, WebReplayJsonContext.Default.AppSessionTimelineRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request?.SessionId))
            return Results.Problem("sessionId is required.", statusCode: StatusCodes.Status400BadRequest);

        var response = await queryService.GetChunksAsync(request.SessionId, request, cancellationToken);
        return ApiSerialization.Write(http, response, WebReplayJsonContext.Default.WebReplayResponse);
    }

    private static async Task<IResult> HandleChunkAsync(
        HttpContext http,
        IWebReplayQueryService queryService,
        CancellationToken cancellationToken)
    {
        WebReplayChunkRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, WebReplayJsonContext.Default.WebReplayChunkRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request?.SessionId) || !Guid.TryParse(request.ChunkId, out var chunkId) || request.StartUnixMs is not > 0)
            return Results.Problem("sessionId, chunkId and startUnixMs are required.", statusCode: StatusCodes.Status400BadRequest);

        var payload = await queryService.GetChunkAsync(request.SessionId, chunkId, request.StartUnixMs.Value, cancellationToken);
        // The stored text was validated as a JSON array of events on receipt, so it goes out as is rather than being parsed and re-encoded.
        return payload is null ? Results.NotFound() : Results.Text(payload, "application/json");
    }
}
