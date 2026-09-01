using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The Ingestion page's pipeline-health section (Planning.md v10):
/// <c>GET /api/ingestion/pipeline?minutes=60</c>. A separate endpoint from
/// <see cref="IngestionEndpoints"/>'s <c>/api/ingestion/stats</c> rather than folded into
/// it - stream/consumer-group health is a live snapshot with no window at all, so cramming
/// both into one response would mean one shared <c>minutes</c> param that only half the
/// payload actually uses.
/// </summary>
public static class PipelineEndpoints
{
    public static IEndpointRouteBuilder MapPipelineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/ingestion/pipeline", HandleGetPipelineStatsAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetPipelineStatsAsync(
        int? minutes,
        HttpContext http,
        IPipelineQueryService queryService,
        CancellationToken cancellationToken)
    {
        var request = new PipelineStatsRequest(minutes ?? 60);
        var response = await queryService.GetPipelineStatsAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, PipelineJsonContext.Default.PipelineStatsResponse);
    }
}
