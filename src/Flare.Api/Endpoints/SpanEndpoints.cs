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
            request = await JsonSerializer.DeserializeAsync(http.Request.Body, SpansJsonContext.Default.SpanSearchRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new SpanSearchRequest();

        var response = await queryService.SearchAsync(request, cancellationToken);
        return Results.Json(response, SpansJsonContext.Default.SpanSearchResponse);
    }

    private static async Task<IResult> HandleGetTraceAsync(
        string traceId,
        ISpanQueryService queryService,
        CancellationToken cancellationToken)
    {
        var trace = await queryService.GetTraceAsync(traceId, cancellationToken);
        return trace is null
            ? Results.NotFound()
            : Results.Json(trace, SpansJsonContext.Default.TraceDto);
    }
}
