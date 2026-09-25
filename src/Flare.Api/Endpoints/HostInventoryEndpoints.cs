using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The Hosts page: <c>POST /api/hosts</c> (inventory table, one row per <c>host.name</c>)
/// and <c>POST /api/hosts/metrics</c> (one host's drill-down charts) - both derived from
/// ingested OTel <c>hostmetrics</c> metrics, see <see cref="HostInventoryQueryBuilder"/>.
/// Distinct from <see cref="HostStatsEndpoints"/>, which reports on the machine Flare.Api
/// itself runs on via its own <c>/proc</c> poller, not on hosts that send telemetry.
/// </summary>
/// <remarks>POST + JSON body, same rationale as <see cref="ServicesEndpoints"/> - consistent with every other filtered query here.</remarks>
public static class HostInventoryEndpoints
{
    public static IEndpointRouteBuilder MapHostInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/hosts", HandleListAsync);
        endpoints.MapPost("/api/hosts/metrics", HandleMetricsAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleListAsync(
        HttpContext http,
        IHostInventoryQueryService queryService,
        CancellationToken cancellationToken)
    {
        HostListRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, HostsJsonContext.Default.HostListRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.ListAsync(request ?? new HostListRequest(), cancellationToken);
        return ApiSerialization.Write(http, response, HostsJsonContext.Default.HostListResponse);
    }

    private static async Task<IResult> HandleMetricsAsync(
        HttpContext http,
        IHostInventoryQueryService queryService,
        CancellationToken cancellationToken)
    {
        HostMetricsRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, HostsJsonContext.Default.HostMetricsRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request?.HostName))
        {
            return Results.Problem("hostName is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetMetricsAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, HostsJsonContext.Default.HostMetricsResponse);
    }
}
