using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The Traces page's Services-tab endpoints: <c>POST /api/services/overview</c> (Table
/// view), <c>POST /api/services/dependencies</c> (Map view - see
/// <see cref="ServiceDependencyQueryBuilder"/>'s remarks), and
/// <c>POST /api/services/breakdown</c> (the Map view's per-node drill-down - see
/// <see cref="ServiceCallBreakdownQueryBuilder"/>'s remarks). Was plain
/// GET-with-query-params (a window length plus, for the drill-down, one service name) until
/// the Services tab's resource-attribute filter chips
/// (docs-internal/planning/roadmap.md's now-removed "Resource-attribute filtering on the
/// Traces &gt; Services tab" item) made every request carry an optional, multi-valued list of
/// <see cref="ResourceAttributeFilter"/> - the same "filters are multi-valued/structured,
/// so POST not GET-with-query-string" reasoning this codebase's CLAUDE.md already
/// documents for <c>/api/logs/*</c>, applied here for the first time.
/// </summary>
public static class ServicesEndpoints
{
    public static IEndpointRouteBuilder MapServicesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/services/overview", HandleGetOverviewAsync);
        endpoints.MapPost("/api/services/dependencies", HandleGetDependenciesAsync);
        endpoints.MapPost("/api/services/breakdown", HandleGetBreakdownAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetOverviewAsync(
        HttpContext http,
        IServiceOverviewQueryService queryService,
        CancellationToken cancellationToken)
    {
        ServiceOverviewRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, ServicesJsonContext.Default.ServiceOverviewRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new ServiceOverviewRequest();

        var response = await queryService.GetOverviewAsync(
            request.WindowMinutes ?? ServiceOverviewQueryBuilder.DefaultWindowMinutes,
            request.ResourceAttributes,
            cancellationToken);
        return ApiSerialization.Write(http, response, ServicesJsonContext.Default.ServiceOverviewResponse);
    }

    private static async Task<IResult> HandleGetDependenciesAsync(
        HttpContext http,
        IServiceDependencyQueryService queryService,
        CancellationToken cancellationToken)
    {
        ServiceDependencyRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, ServicesJsonContext.Default.ServiceDependencyRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new ServiceDependencyRequest();

        var response = await queryService.GetGraphAsync(
            request.WindowMinutes ?? ServiceDependencyQueryBuilder.DefaultWindowMinutes,
            request.ResourceAttributes,
            cancellationToken);
        return ApiSerialization.Write(http, response, ServicesJsonContext.Default.ServiceDependencyGraphResponse);
    }

    private static async Task<IResult> HandleGetBreakdownAsync(
        HttpContext http,
        IServiceCallBreakdownQueryService queryService,
        CancellationToken cancellationToken)
    {
        ServiceCallBreakdownRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, ServicesJsonContext.Default.ServiceCallBreakdownRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Service))
        {
            return Results.Problem("service is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetBreakdownAsync(
            request.Service,
            request.WindowMinutes ?? ServiceCallBreakdownQueryBuilder.DefaultWindowMinutes,
            request.ResourceAttributes,
            cancellationToken);
        return ApiSerialization.Write(http, response, ServicesJsonContext.Default.ServiceCallBreakdownResponse);
    }
}
