using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The <c>/sessions</c> page's Query API: <c>POST /api/app-sessions/list</c>, one row per client-app
/// session grouped from spans' <c>session.id</c>. Not to be confused with
/// <see cref="SessionEndpoints"/>, which manages the caller's own dashboard login sessions.
/// See docs-internal/adr/0167-app-sessions-view.md.
/// </summary>
public static class AppSessionEndpoints
{
    public static IEndpointRouteBuilder MapAppSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/app-sessions/list", HandleListAsync);
        endpoints.MapPost("/api/app-sessions/timeline", HandleTimelineAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleTimelineAsync(
        HttpContext http,
        IAppSessionQueryService queryService,
        CancellationToken cancellationToken)
    {
        AppSessionTimelineRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AppSessionJsonContext.Default.AppSessionTimelineRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request?.SessionId))
            return Results.Problem("sessionId is required.", statusCode: StatusCodes.Status400BadRequest);

        var response = await queryService.GetTimelineAsync(request.SessionId, request, cancellationToken);
        return ApiSerialization.Write(http, response, AppSessionJsonContext.Default.AppSessionTimelineResponse);
    }

    private static async Task<IResult> HandleListAsync(
        HttpContext http,
        IAppSessionQueryService queryService,
        CancellationToken cancellationToken)
    {
        AppSessionsRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AppSessionJsonContext.Default.AppSessionsRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetSessionsAsync(request ?? new AppSessionsRequest(), cancellationToken);
        return ApiSerialization.Write(http, response, AppSessionJsonContext.Default.AppSessionsResponse);
    }
}
