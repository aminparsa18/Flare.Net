using Flare.Api.Json;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The Indexing page's endpoints: <c>GET /api/indexing/stats</c> and
/// <c>GET /api/indexing/cluster</c>. No query params on either - see
/// <see cref="IndexingQueryService"/>/<see cref="ClusterQueryService"/>'s remarks for why
/// there's no filter to accept.
/// </summary>
public static class IndexingEndpoints
{
    public static IEndpointRouteBuilder MapIndexingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/indexing/stats", HandleGetStatsAsync);
        endpoints.MapGet("/api/indexing/cluster", HandleGetClusterStatusAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetStatsAsync(
        HttpContext http,
        IIndexingQueryService queryService,
        CancellationToken cancellationToken)
    {
        var response = await queryService.GetStatsAsync(cancellationToken);
        return ApiSerialization.Write(http, response, IndexingJsonContext.Default.IndexingStatsResponse);
    }

    private static async Task<IResult> HandleGetClusterStatusAsync(
        HttpContext http,
        IClusterStatusService clusterStatusService,
        CancellationToken cancellationToken)
    {
        var response = await clusterStatusService.GetStatusAsync(cancellationToken);
        return ApiSerialization.Write(http, response, IndexingJsonContext.Default.ClusterStatusResponse);
    }
}
