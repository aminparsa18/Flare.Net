using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// <c>POST /api/pods/metrics</c> - one Kubernetes pod's CPU/memory over a window, from the
/// OTel <c>kubeletstats</c> receiver (see <see cref="PodMetricsQueryBuilder"/>). The pod
/// counterpart of <see cref="HostInventoryEndpoints"/>' <c>/api/hosts/metrics</c>, used by
/// the log event detail view for logs carrying <c>k8s.pod.name</c>.
/// </summary>
public static class PodMetricsEndpoints
{
    public static IEndpointRouteBuilder MapPodMetricsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/pods/metrics", HandleMetricsAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleMetricsAsync(
        HttpContext http,
        IPodMetricsQueryService queryService,
        CancellationToken cancellationToken)
    {
        PodMetricsRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, PodsJsonContext.Default.PodMetricsRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request?.PodName))
        {
            return Results.Problem("podName is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetMetricsAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, PodsJsonContext.Default.PodMetricsResponse);
    }
}
