using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Api.Query.LogQl;

namespace Flare.Api.Endpoints;

/// <summary>The Query API: <c>POST /api/logs/search</c>, <c>POST /api/logs/aggregate</c>, and <c>POST /api/logs/patterns</c>.</summary>
/// <remarks>
/// POST + JSON body rather than GET + query string for both - filters are
/// multi-valued/structured (service lists, level lists, attribute key/value pairs),
/// which doesn't fit cleanly in a query string. The live-tail streaming endpoint
/// (<c>GET /api/logs/tail</c>) lives separately in <see cref="LogTailEndpoints"/> - a
/// WebSocket upgrade, not a request/response route, so it doesn't fit this file's shape.
/// </remarks>
public static class LogsEndpoints
{
    public static IEndpointRouteBuilder MapLogsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/logs/search", HandleSearchAsync);
        endpoints.MapPost("/api/logs/aggregate", HandleAggregateAsync);
        endpoints.MapPost("/api/logs/patterns", HandlePatternsAsync);
        endpoints.MapPost("/api/logs/numeric-attribute-keys", HandleNumericAttributeKeysAsync);
        endpoints.MapPost("/api/logs/value-distribution", HandleValueDistributionAsync);
        endpoints.MapPost("/api/logs/query", HandleQlQueryAsync);
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
            request = await ApiSerialization.ReadAsync(http, LogsJsonContext.Default.LogSearchRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new LogSearchRequest();

        var response = await queryService.SearchAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, LogsJsonContext.Default.LogSearchResponse);
    }

    private static async Task<IResult> HandleAggregateAsync(
        HttpContext http,
        ILogQueryService queryService,
        CancellationToken cancellationToken)
    {
        LogAggregateRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, LogsJsonContext.Default.LogAggregateRequest, cancellationToken);
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
            return ApiSerialization.Write(http, response, LogsJsonContext.Default.LogAggregateResponse);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> HandlePatternsAsync(
        HttpContext http,
        ILogQueryService queryService,
        CancellationToken cancellationToken)
    {
        LogPatternRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, LogsJsonContext.Default.LogPatternRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new LogPatternRequest();

        var response = await queryService.GetPatternsAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, LogsJsonContext.Default.LogPatternResponse);
    }

    private static async Task<IResult> HandleNumericAttributeKeysAsync(
        HttpContext http,
        ILogQueryService queryService,
        CancellationToken cancellationToken)
    {
        LogAttributeKeysRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, LogsJsonContext.Default.LogAttributeKeysRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new LogAttributeKeysRequest();

        var response = await queryService.GetNumericAttributeKeysAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, LogsJsonContext.Default.LogAttributeKeysResponse);
    }

    private static async Task<IResult> HandleValueDistributionAsync(
        HttpContext http,
        ILogQueryService queryService,
        CancellationToken cancellationToken)
    {
        LogValueDistributionRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, LogsJsonContext.Default.LogValueDistributionRequest, cancellationToken);
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
            var response = await queryService.GetValueDistributionAsync(request, cancellationToken);
            return ApiSerialization.Write(http, response, LogsJsonContext.Default.LogValueDistributionResponse);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> HandleQlQueryAsync(
        HttpContext http,
        ILogQueryService queryService,
        CancellationToken cancellationToken)
    {
        LogQlQueryRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, LogsJsonContext.Default.LogQlQueryRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Query))
        {
            return Results.Problem("A non-empty \"query\" is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var response = await queryService.RunQlQueryAsync(request, cancellationToken);
            return ApiSerialization.Write(http, response, LogsJsonContext.Default.LogQlQueryResponse);
        }
        catch (LogQlParseException ex)
        {
            // ex.Message is written to be user-facing (see LogQlParseException's remarks)
            // - reaches SqlQueryRow.svelte verbatim via runLogQlQuery's Problem-body read.
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
