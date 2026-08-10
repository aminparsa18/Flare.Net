using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The Ingestion page's one endpoint: <c>GET /api/ingestion/stats?minutes=60</c>. A plain
/// GET with an optional query param, not the POST+JSON-body convention <see cref="LogsEndpoints"/>/
/// <see cref="SpanEndpoints"/>/<see cref="MetricsEndpoints"/> use for their structured
/// filters - this endpoint's only input is one bounded integer, so a query string is
/// simpler and there's no real filter object to justify a body.
/// </summary>
public static class IngestionEndpoints
{
    public static IEndpointRouteBuilder MapIngestionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/ingestion/stats", HandleGetStatsAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetStatsAsync(
        int? minutes,
        IIngestionStatsQueryService queryService,
        CancellationToken cancellationToken)
    {
        var request = new IngestionStatsRequest(minutes ?? 60);
        var response = await queryService.GetStatsAsync(request, cancellationToken);
        return Results.Json(response, IngestionJsonContext.Default.IngestionStatsResponse);
    }
}
