using Flare.Api.Json;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The Services landing page's one endpoint: <c>GET /api/services/overview?windowMinutes=15</c>.
/// Same plain-GET-with-one-bounded-int-query-param convention as
/// <see cref="IngestionEndpoints"/> (see its own remarks) - the only input here is one
/// window length, so there's no structured filter to justify a POST body.
/// </summary>
public static class ServicesEndpoints
{
    public static IEndpointRouteBuilder MapServicesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/services/overview", HandleGetOverviewAsync);
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
}
