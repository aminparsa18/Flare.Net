using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The traces Query API: <c>POST /api/spans/search</c> (root-span list, backs the
/// dashboard's trace list view) and <c>GET /api/traces/{traceId}</c> (every span in one
/// trace, backs the waterfall view). Same POST+JSON-body-for-structured-filters
/// rationale as <see cref="LogsEndpoints"/> for the search route; the trace-by-id route
/// is a simple path-parameter lookup, so it's a plain GET, same convention
/// <see cref="AlertEndpoints"/> uses for <c>/api/alerts/{id}</c>.
/// </summary>
public static class SpanEndpoints
{
    public static IEndpointRouteBuilder MapSpanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/spans/search", HandleSearchAsync);
        endpoints.MapGet("/api/traces/{traceId}", HandleGetTraceAsync);
        endpoints.MapGet("/api/traces/{traceId}/spans/{spanId}/children", HandleGetSubtreeAsync);
        endpoints.MapPost("/api/spans/attribute-values", HandleAttributeValuesAsync);
        endpoints.MapPost("/api/spans/duration-percentile", HandleDurationPercentileAsync);
        endpoints.MapPost("/api/traces/structure/validate", HandleValidateStructureAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleSearchAsync(
        HttpContext http,
        ISpanQueryService queryService,
        CancellationToken cancellationToken)
    {
        SpanSearchRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, SpansJsonContext.Default.SpanSearchRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new SpanSearchRequest();

        try
        {
            var response = await queryService.SearchAsync(request, cancellationToken);
            return ApiSerialization.Write(http, response, SpansJsonContext.Default.SpanSearchResponse);
        }
        catch (ArgumentException ex)
        {
            // An invalid SpanFilter.Structure (TraceStructureSqlBuilder.Validate).
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> HandleGetTraceAsync(
        string traceId,
        HttpContext http,
        ISpanQueryService queryService,
        CancellationToken cancellationToken)
    {
        var trace = await queryService.GetTraceAsync(traceId, cancellationToken);
        return trace is null
            ? Results.NotFound()
            : ApiSerialization.Write(http, trace, SpansJsonContext.Default.TraceDto);
    }

    private static async Task<IResult> HandleGetSubtreeAsync(
        string traceId,
        string spanId,
        HttpContext http,
        ISpanQueryService queryService,
        CancellationToken cancellationToken)
    {
        var subtree = await queryService.GetSubtreeAsync(traceId, spanId, cancellationToken);
        return ApiSerialization.Write(http, subtree, SpansJsonContext.Default.TraceDto);
    }

    private static async Task<IResult> HandleAttributeValuesAsync(
        HttpContext http,
        ISpanQueryService queryService,
        CancellationToken cancellationToken)
    {
        SpanAttributeValuesRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, SpansJsonContext.Default.SpanAttributeValuesRequest, cancellationToken);
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
            var response = await queryService.GetAttributeValuesAsync(request, cancellationToken);
            ApiSerialization.SetAutocompleteCacheControl(http);
            return ApiSerialization.Write(http, response, SpansJsonContext.Default.SpanAttributeValuesResponse);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> HandleDurationPercentileAsync(
        HttpContext http,
        ISpanQueryService queryService,
        CancellationToken cancellationToken)
    {
        SpanDurationPercentileRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, SpansJsonContext.Default.SpanDurationPercentileRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null || string.IsNullOrEmpty(request.Name))
        {
            return Results.Problem("Name is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetDurationPercentileAsync(request, cancellationToken);
        return Results.Json(response, SpansJsonContext.Default.SpanDurationPercentileResponse);
    }

    /// <summary>
    /// Checks a <see cref="TraceStructureFilter"/> without querying anything - 204, or 400
    /// with the reason. The dashboard calls it before applying a structure, so an invalid one
    /// never reaches the search and facet requests that all share the filter.
    /// </summary>
    private static async Task<IResult> HandleValidateStructureAsync(HttpContext http, CancellationToken cancellationToken)
    {
        try
        {
            var structure = await ApiSerialization.ReadAsync(http, SpansJsonContext.Default.TraceStructureFilter, cancellationToken);
            if (structure is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            TraceStructureSqlBuilder.Validate(structure);
            return Results.NoContent();
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
