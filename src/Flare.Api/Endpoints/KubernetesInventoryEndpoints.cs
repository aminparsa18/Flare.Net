using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The Kubernetes page: <c>POST /api/kubernetes/nodes</c> (Nodes table),
/// <c>POST /api/kubernetes/nodes/metrics</c> (one node's drill-down charts) and
/// <c>POST /api/kubernetes/pods</c> (Pods table), <c>/workloads</c>(<c>/metrics</c>),
/// <c>/namespaces</c> and <c>/volumes</c>(<c>/metrics</c>) - all derived from ingested OTel
/// <c>kubeletstats</c>/<c>k8s_cluster</c> metrics, see <see cref="KubernetesInventoryQueryBuilder"/>
/// and <see cref="KubernetesWorkloadQueryBuilder"/>.
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
        endpoints.MapPost("/api/kubernetes/workloads", HandleWorkloadsAsync);
        endpoints.MapPost("/api/kubernetes/workloads/metrics", HandleWorkloadMetricsAsync);
        endpoints.MapPost("/api/kubernetes/namespaces", HandleNamespacesAsync);
        endpoints.MapPost("/api/kubernetes/volumes", HandleVolumesAsync);
        endpoints.MapPost("/api/kubernetes/volumes/metrics", HandleVolumeMetricsAsync);
        return endpoints;
    }

    private static IResult UnknownKind(string? kind) => Results.Problem(
        $"Unknown workload kind '{kind}'. Expected one of: {string.Join(", ", KubernetesWorkloadQueryBuilder.Kinds.Select(k => k.Kind))}.",
        statusCode: StatusCodes.Status400BadRequest);

    private static async Task<IResult> HandleWorkloadsAsync(
        HttpContext http,
        IKubernetesInventoryQueryService queryService,
        CancellationToken cancellationToken)
    {
        KubernetesWorkloadListRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, KubernetesJsonContext.Default.KubernetesWorkloadListRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (KubernetesWorkloadQueryBuilder.ResolveKind(request?.Kind) is not { } kind)
        {
            return UnknownKind(request?.Kind);
        }

        var response = await queryService.ListWorkloadsAsync(kind, request!, cancellationToken);
        return ApiSerialization.Write(http, response, KubernetesJsonContext.Default.KubernetesWorkloadListResponse);
    }

    private static async Task<IResult> HandleWorkloadMetricsAsync(
        HttpContext http,
        IKubernetesInventoryQueryService queryService,
        CancellationToken cancellationToken)
    {
        KubernetesWorkloadMetricsRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, KubernetesJsonContext.Default.KubernetesWorkloadMetricsRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (KubernetesWorkloadQueryBuilder.ResolveKind(request?.Kind) is not { } kind)
        {
            return UnknownKind(request?.Kind);
        }

        if (string.IsNullOrWhiteSpace(request!.Namespace) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.Problem("namespace and name are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetWorkloadMetricsAsync(kind, request, cancellationToken);
        return ApiSerialization.Write(http, response, KubernetesJsonContext.Default.KubernetesWorkloadMetricsResponse);
    }

    private static async Task<IResult> HandleNamespacesAsync(
        HttpContext http,
        IKubernetesInventoryQueryService queryService,
        CancellationToken cancellationToken)
    {
        KubernetesNamespaceListRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, KubernetesJsonContext.Default.KubernetesNamespaceListRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.ListNamespacesAsync(request ?? new KubernetesNamespaceListRequest(), cancellationToken);
        return ApiSerialization.Write(http, response, KubernetesJsonContext.Default.KubernetesNamespaceListResponse);
    }

    private static async Task<IResult> HandleVolumesAsync(
        HttpContext http,
        IKubernetesInventoryQueryService queryService,
        CancellationToken cancellationToken)
    {
        KubernetesVolumeListRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, KubernetesJsonContext.Default.KubernetesVolumeListRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.ListVolumesAsync(request ?? new KubernetesVolumeListRequest(), cancellationToken);
        return ApiSerialization.Write(http, response, KubernetesJsonContext.Default.KubernetesVolumeListResponse);
    }

    private static async Task<IResult> HandleVolumeMetricsAsync(
        HttpContext http,
        IKubernetesInventoryQueryService queryService,
        CancellationToken cancellationToken)
    {
        KubernetesVolumeMetricsRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, KubernetesJsonContext.Default.KubernetesVolumeMetricsRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request?.Namespace) || string.IsNullOrWhiteSpace(request.PodName) || string.IsNullOrWhiteSpace(request.VolumeName))
        {
            return Results.Problem("namespace, podName and volumeName are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetVolumeMetricsAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, KubernetesJsonContext.Default.KubernetesVolumeMetricsResponse);
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
