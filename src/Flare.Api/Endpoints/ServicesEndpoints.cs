using Flare.Api.Json;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The Traces page's Services-tab endpoints: <c>GET /api/services/overview?windowMinutes=15</c>
/// (Table view), <c>GET /api/services/dependencies?windowMinutes=15</c> (Map view - see
/// <see cref="ServiceDependencyQueryBuilder"/>'s remarks), and
/// <c>GET /api/services/breakdown?service=X&amp;windowMinutes=15</c> (the Map view's per-node
/// drill-down - see <see cref="ServiceCallBreakdownQueryBuilder"/>'s remarks). Same
/// plain-GET-with-query-params convention as <see cref="IngestionEndpoints"/> (see its own
/// remarks) throughout - a window length plus, for the drill-down, one service name -
/// nothing structured enough here to justify a POST body.
/// </summary>
public static class ServicesEndpoints
{
    public static IEndpointRouteBuilder MapServicesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/services/overview", HandleGetOverviewAsync);
        endpoints.MapGet("/api/services/dependencies", HandleGetDependenciesAsync);
        endpoints.MapGet("/api/services/breakdown", HandleGetBreakdownAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetOverviewAsync(
        int? windowMinutes,
        HttpContext http,
        IServiceOverviewQueryService queryService,
        CancellationToken cancellationToken)
    {
        var response = await queryService.GetOverviewAsync(windowMinutes ?? ServiceOverviewQueryBuilder.DefaultWindowMinutes, cancellationToken);
        return ApiSerialization.Write(http, response, ServicesJsonContext.Default.ServiceOverviewResponse);
    }

    private static async Task<IResult> HandleGetDependenciesAsync(
        int? windowMinutes,
        HttpContext http,
        IServiceDependencyQueryService queryService,
        CancellationToken cancellationToken)
    {
        var response = await queryService.GetGraphAsync(windowMinutes ?? ServiceDependencyQueryBuilder.DefaultWindowMinutes, cancellationToken);
        return ApiSerialization.Write(http, response, ServicesJsonContext.Default.ServiceDependencyGraphResponse);
    }

    private static async Task<IResult> HandleGetBreakdownAsync(
        string? service,
        int? windowMinutes,
        HttpContext http,
        IServiceCallBreakdownQueryService queryService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(service))
        {
            return Results.Problem("service is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetBreakdownAsync(service, windowMinutes ?? ServiceCallBreakdownQueryBuilder.DefaultWindowMinutes, cancellationToken);
        return ApiSerialization.Write(http, response, ServicesJsonContext.Default.ServiceCallBreakdownResponse);
    }
}
