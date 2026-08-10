using Flare.Api.Json;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The Indexing page's one endpoint: <c>GET /api/indexing/stats</c>. No query params - see
/// <see cref="IndexingQueryService"/>'s remarks for why there's no filter to accept.
/// </summary>
public static class IndexingEndpoints
{
    public static IEndpointRouteBuilder MapIndexingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/indexing/stats", HandleGetStatsAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetStatsAsync(
        IIndexingQueryService queryService,
        CancellationToken cancellationToken)
    {
        var response = await queryService.GetStatsAsync(cancellationToken);
        return Results.Json(response, IndexingJsonContext.Default.IndexingStatsResponse);
    }
}
