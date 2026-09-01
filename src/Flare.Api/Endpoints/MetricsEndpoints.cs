using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>The Query API: <c>POST /api/metrics/names</c> (discovery), <c>POST /api/metrics/attribute-keys</c> ("Group by" key discovery), and <c>POST /api/metrics/query</c> (time-bucketed series).</summary>
/// <remarks>
/// POST + JSON body for both, same rationale as <see cref="LogsEndpoints"/>/
/// <see cref="SpanEndpoints"/>'s search routes - filters are structured (time range,
/// service lists, attribute key/value pairs). Unlike logs/spans there's no simple
/// path-parameter "by id" lookup here to justify a plain GET (see
/// <see cref="SpanEndpoints"/>'s trace-by-id route for that shape) - metrics discovery
/// itself needs a scope (time range + services), not just an id.
/// </remarks>
public static class MetricsEndpoints
{
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/metrics/names", HandleNamesAsync);
        endpoints.MapPost("/api/metrics/attribute-keys", HandleAttributeKeysAsync);
        endpoints.MapPost("/api/metrics/query", HandleQueryAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleNamesAsync(
        HttpContext http,
        IMetricQueryService queryService,
        CancellationToken cancellationToken)
    {
        MetricNamesRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, MetricsJsonContext.Default.MetricNamesRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new MetricNamesRequest();

        var response = await queryService.GetNamesAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, MetricsJsonContext.Default.MetricNamesResponse);
    }

    private static async Task<IResult> HandleAttributeKeysAsync(
        HttpContext http,
        IMetricQueryService queryService,
        CancellationToken cancellationToken)
    {
        MetricAttributeKeysRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, MetricsJsonContext.Default.MetricAttributeKeysRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetAttributeKeysAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, MetricsJsonContext.Default.MetricAttributeKeysResponse);
    }

    private static async Task<IResult> HandleQueryAsync(
        HttpContext http,
        IMetricQueryService queryService,
        CancellationToken cancellationToken)
    {
        MetricQueryRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, MetricsJsonContext.Default.MetricQueryRequest, cancellationToken);
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
            var response = await queryService.QueryAsync(request, cancellationToken);
            return ApiSerialization.Write(http, response, MetricsJsonContext.Default.MetricQueryResponse);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
