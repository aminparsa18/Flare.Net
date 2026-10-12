using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.SourceMaps;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The <c>/errors</c> page's Query API: <c>POST /api/errors/groups</c> (the ranked
/// exception-type/message rollup) and <c>POST /api/errors/occurrences</c> (one group's
/// click-through drill-down - sample traces + stack traces), plus
/// <c>POST /api/errors/facet-values</c> (the facet sidebar's per-value counts). Same POST+JSON-body-for-
/// structured-filters convention as <see cref="SpanEndpoints"/>/<see cref="LogsEndpoints"/> -
/// both bodies carry an <see cref="ExceptionFilter"/>, not just a couple of scalars.
/// </summary>
public static class ExceptionEndpoints
{
    public static IEndpointRouteBuilder MapExceptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/errors/groups", HandleGetGroupsAsync);
        endpoints.MapPost("/api/errors/occurrences", HandleGetOccurrencesAsync);
        endpoints.MapPost("/api/errors/facet-values", HandleGetFacetValuesAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetGroupsAsync(
        HttpContext http,
        IExceptionQueryService queryService,
        CancellationToken cancellationToken)
    {
        ExceptionGroupsRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, ErrorsJsonContext.Default.ExceptionGroupsRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new ExceptionGroupsRequest();

        var response = await queryService.GetGroupsAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, ErrorsJsonContext.Default.ExceptionGroupsResponse);
    }

    private static async Task<IResult> HandleGetOccurrencesAsync(
        HttpContext http,
        IExceptionQueryService queryService,
        IStackTraceSymbolicator symbolicator,
        CancellationToken cancellationToken)
    {
        ExceptionOccurrencesRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, ErrorsJsonContext.Default.ExceptionOccurrencesRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetOccurrencesAsync(request, cancellationToken);
        response = response with { Occurrences = await SymbolicateAsync(response.Occurrences, symbolicator, cancellationToken) };
        return ApiSerialization.Write(http, response, ErrorsJsonContext.Default.ExceptionOccurrencesResponse);
    }

    /// <summary>Rewrites browser stack frames from uploaded source maps (ADR-0152); an occurrence nothing matches is returned as-is.</summary>
    private static async Task<IReadOnlyList<ExceptionOccurrence>> SymbolicateAsync(IReadOnlyList<ExceptionOccurrence> occurrences, IStackTraceSymbolicator symbolicator, CancellationToken cancellationToken)
    {
        var result = new List<ExceptionOccurrence>(occurrences.Count);
        foreach (var occurrence in occurrences)
        {
            var (stacktrace, symbolicated) = await symbolicator.SymbolicateAsync(occurrence.ServiceName, [occurrence.ServiceVersion, occurrence.Revision], occurrence.Stacktrace, cancellationToken);
            result.Add(symbolicated ? occurrence with { Stacktrace = stacktrace, Symbolicated = true } : occurrence);
        }

        return result;
    }

    private static async Task<IResult> HandleGetFacetValuesAsync(
        HttpContext http,
        IExceptionQueryService queryService,
        CancellationToken cancellationToken)
    {
        ExceptionFacetValuesRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, ErrorsJsonContext.Default.ExceptionFacetValuesRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new ExceptionFacetValuesRequest();

        try
        {
            var response = await queryService.GetFacetValuesAsync(request, cancellationToken);
            return ApiSerialization.Write(http, response, ErrorsJsonContext.Default.ExceptionFacetValuesResponse);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
