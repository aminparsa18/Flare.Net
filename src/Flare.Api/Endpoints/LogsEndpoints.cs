using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>The Query API: <c>POST /api/logs/search</c> and <c>POST /api/logs/aggregate</c>.</summary>
/// <remarks>
/// POST + JSON body rather than GET + query string for both - filters are
/// multi-valued/structured (service lists, level lists, attribute key/value pairs),
/// which doesn't fit cleanly in a query string. No live-tail/streaming endpoint here -
/// that's a separate, later roadmap item.
/// </remarks>
public static class LogsEndpoints
{
    public static IEndpointRouteBuilder MapLogsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/logs/search", HandleSearchAsync);
        endpoints.MapPost("/api/logs/aggregate", HandleAggregateAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleSearchAsync(
        HttpContext http,
        ILogQueryService queryService,
        CancellationToken cancellationToken)
    {
        LogSearchRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(http.Request.Body, LogsJsonContext.Default.LogSearchRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new LogSearchRequest();

        var response = await queryService.SearchAsync(request, cancellationToken);
        return Results.Json(response, LogsJsonContext.Default.LogSearchResponse);
    }

    private static async Task<IResult> HandleAggregateAsync(
        HttpContext http,
        ILogQueryService queryService,
        CancellationToken cancellationToken)
    {
        LogAggregateRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(http.Request.Body, LogsJsonContext.Default.LogAggregateRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var response = await queryService.AggregateAsync(request, cancellationToken);
            return Results.Json(response, LogsJsonContext.Default.LogAggregateResponse);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
