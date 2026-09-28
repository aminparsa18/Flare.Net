using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The Kubernetes page: <c>POST /api/kubernetes/nodes</c> (Nodes table),
/// <c>POST /api/kubernetes/nodes/metrics</c> (one node's drill-down charts) and
/// <c>POST /api/kubernetes/pods</c> (Pods table) - all derived from ingested OTel
/// <c>kubeletstats</c>/<c>k8s_cluster</c> metrics, see <see cref="KubernetesInventoryQueryBuilder"/>.
/// A pod's drill-down is <see cref="PodMetricsEndpoints"/>' existing <c>/api/pods/metrics</c>.
/// Distinct from <see cref="ResourceGraphEndpoints"/>' Kubernetes provider, which polls the
/// Kubernetes API for Flare's own pods only.
/// </summary>
public static class KubernetesInventoryEndpoints
{
    public static IEndpointRouteBuilder MapKubernetesInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/kubernetes/nodes", HandleNodesAsync);
        endpoints.MapPost("/api/kubernetes/nodes/metrics", HandleNodeMetricsAsync);
        endpoints.MapPost("/api/kubernetes/pods", HandlePodsAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleNodesAsync(
        HttpContext http,
        IKubernetesInventoryQueryService queryService,
        CancellationToken cancellationToken)
    {
        KubernetesNodeListRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, KubernetesJsonContext.Default.KubernetesNodeListRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.ListNodesAsync(request ?? new KubernetesNodeListRequest(), cancellationToken);
        return ApiSerialization.Write(http, response, KubernetesJsonContext.Default.KubernetesNodeListResponse);
    }

    private static async Task<IResult> HandleNodeMetricsAsync(
        HttpContext http,
        IKubernetesInventoryQueryService queryService,
        CancellationToken cancellationToken)
    {
        KubernetesNodeMetricsRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, KubernetesJsonContext.Default.KubernetesNodeMetricsRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request?.NodeName))
        {
            return Results.Problem("nodeName is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetNodeMetricsAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, KubernetesJsonContext.Default.KubernetesNodeMetricsResponse);
    }

    private static async Task<IResult> HandlePodsAsync(
        HttpContext http,
        IKubernetesInventoryQueryService queryService,
        CancellationToken cancellationToken)
    {
        KubernetesPodListRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, KubernetesJsonContext.Default.KubernetesPodListRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.ListPodsAsync(request ?? new KubernetesPodListRequest(), cancellationToken);
        return ApiSerialization.Write(http, response, KubernetesJsonContext.Default.KubernetesPodListResponse);
    }
}
