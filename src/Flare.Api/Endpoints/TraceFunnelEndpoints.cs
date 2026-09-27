using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// Trace funnels: <c>POST /api/traces/funnel</c> (per-step trace counts, errors and
/// step-to-step latency) and <c>POST /api/traces/funnel/traces</c> (the traces behind one
/// step's reached/dropped/errored figure). POST for the structured step list, same
/// convention as <see cref="SpanEndpoints"/>' search route. See
/// docs-internal/adr/0067-trace-funnels.md.
/// </summary>
public static class TraceFunnelEndpoints
{
    public static IEndpointRouteBuilder MapTraceFunnelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/traces/funnel", HandleFunnelAsync);
        endpoints.MapPost("/api/traces/funnel/traces", HandleTracesAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleFunnelAsync(
        HttpContext http,
        ITraceFunnelQueryService queryService,
        CancellationToken cancellationToken)
    {
        TraceFunnelRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, TraceFunnelJsonContext.Default.TraceFunnelRequest, cancellationToken);
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
            var response = await queryService.GetFunnelAsync(request, cancellationToken);
            return ApiSerialization.Write(http, response, TraceFunnelJsonContext.Default.TraceFunnelResponse);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> HandleTracesAsync(
        HttpContext http,
        ITraceFunnelQueryService queryService,
        CancellationToken cancellationToken)
    {
        TraceFunnelTracesRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, TraceFunnelJsonContext.Default.TraceFunnelTracesRequest, cancellationToken);
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
            var response = await queryService.GetTracesAsync(request, cancellationToken);
            return ApiSerialization.Write(http, response, TraceFunnelJsonContext.Default.TraceFunnelTracesResponse);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
