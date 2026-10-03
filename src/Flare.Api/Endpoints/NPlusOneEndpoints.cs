using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// N+1 query detection: <c>POST /api/traces/n-plus-one</c> lists the database statements one
/// parent span repeated within a trace, worst first. POST for the body, same convention as
/// <see cref="TraceFunnelEndpoints"/>.
/// </summary>
public static class NPlusOneEndpoints
{
    public static IEndpointRouteBuilder MapNPlusOneEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/traces/n-plus-one", HandleAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        INPlusOneQueryService queryService,
        CancellationToken cancellationToken)
    {
        NPlusOneRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, NPlusOneJsonContext.Default.NPlusOneRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetOffendersAsync(request ?? new NPlusOneRequest(), cancellationToken);
        return ApiSerialization.Write(http, response, NPlusOneJsonContext.Default.NPlusOneResponse);
    }
}
