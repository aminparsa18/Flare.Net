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
        endpoints.MapPost("/api/app-sessions/release-health", HandleReleaseHealthAsync);
        endpoints.MapPost("/api/app-sessions/release-health/trend", HandleReleaseHealthTrendAsync);
        endpoints.MapPost("/api/app-sessions/performance", HandlePerformanceAsync);
        endpoints.MapPost("/api/app-sessions/timeline", HandleTimelineAsync);
        endpoints.MapPost("/api/app-sessions/screenshot", HandleScreenshotAsync);
        endpoints.MapPost("/api/app-sessions/replay", HandleReplayAsync);
        endpoints.MapPost("/api/app-sessions/replay/frame", HandleReplayFrameAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleReleaseHealthAsync(
        HttpContext http,
        IAppSessionQueryService queryService,
        CancellationToken cancellationToken)
    {
        ReleaseHealthRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AppSessionJsonContext.Default.ReleaseHealthRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetReleaseHealthAsync(request ?? new ReleaseHealthRequest(), cancellationToken);
        return ApiSerialization.Write(http, response, AppSessionJsonContext.Default.ReleaseHealthResponse);
    }

    private static async Task<IResult> HandleReleaseHealthTrendAsync(
        HttpContext http,
        IAppSessionQueryService queryService,
        CancellationToken cancellationToken)
    {
        ReleaseHealthTrendRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AppSessionJsonContext.Default.ReleaseHealthTrendRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetReleaseHealthTrendAsync(request ?? new ReleaseHealthTrendRequest(), cancellationToken);
        return ApiSerialization.Write(http, response, AppSessionJsonContext.Default.ReleaseHealthTrendResponse);
    }

    private static async Task<IResult> HandlePerformanceAsync(
        HttpContext http,
        IAppSessionQueryService queryService,
        CancellationToken cancellationToken)
    {
        AppPerformanceRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AppSessionJsonContext.Default.AppPerformanceRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetPerformanceAsync(request ?? new AppPerformanceRequest(), cancellationToken);
        return ApiSerialization.Write(http, response, AppSessionJsonContext.Default.AppPerformanceResponse);
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

    private static async Task<IResult> HandleScreenshotAsync(
        HttpContext http,
        IAppSessionQueryService queryService,
        CancellationToken cancellationToken)
    {
        AppSessionScreenshotRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AppSessionJsonContext.Default.AppSessionScreenshotRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request?.SessionId) || string.IsNullOrWhiteSpace(request.SpanId))
            return Results.Problem("sessionId and spanId are required.", statusCode: StatusCodes.Status400BadRequest);

        var window = new AppSessionTimelineRequest { SessionId = request.SessionId, FromUnixMs = request.FromUnixMs, ToUnixMs = request.ToUnixMs };
        var shot = await queryService.GetScreenshotAsync(request.SessionId, request.SpanId, window, cancellationToken);
        return shot is null
            ? Results.NotFound()
            : ApiSerialization.Write(http, shot, AppSessionJsonContext.Default.AppSessionScreenshotResponse);
    }

    private static async Task<IResult> HandleReplayAsync(
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

        var response = await queryService.GetReplayAsync(request.SessionId, request, cancellationToken);
        return ApiSerialization.Write(http, response, AppSessionJsonContext.Default.AppSessionReplayResponse);
    }

    private static async Task<IResult> HandleReplayFrameAsync(
        HttpContext http,
        IAppSessionQueryService queryService,
        CancellationToken cancellationToken)
    {
        AppSessionReplayFrameRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AppSessionJsonContext.Default.AppSessionReplayFrameRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request?.SessionId) || request.TimeUnixMs is not > 0)
            return Results.Problem("sessionId and timeUnixMs are required.", statusCode: StatusCodes.Status400BadRequest);

        var frame = await queryService.GetReplayFrameAsync(request.SessionId, request.TimeUnixMs.Value, cancellationToken);
        return frame is null
            ? Results.NotFound()
            : ApiSerialization.Write(http, frame, AppSessionJsonContext.Default.AppSessionScreenshotResponse);
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
