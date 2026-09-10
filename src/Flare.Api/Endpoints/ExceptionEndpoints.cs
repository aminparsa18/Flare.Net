using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The <c>/errors</c> page's Query API: <c>POST /api/errors/groups</c> (the ranked
/// exception-type/message rollup) and <c>POST /api/errors/occurrences</c> (one group's
/// click-through drill-down - sample traces + stack traces). Same POST+JSON-body-for-
/// structured-filters convention as <see cref="SpanEndpoints"/>/<see cref="LogsEndpoints"/> -
/// both bodies carry an <see cref="ExceptionFilter"/>, not just a couple of scalars.
/// </summary>
public static class ExceptionEndpoints
{
    public static IEndpointRouteBuilder MapExceptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/errors/groups", HandleGetGroupsAsync);
        endpoints.MapPost("/api/errors/occurrences", HandleGetOccurrencesAsync);
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
        return ApiSerialization.Write(http, response, ErrorsJsonContext.Default.ExceptionOccurrencesResponse);
    }
}
