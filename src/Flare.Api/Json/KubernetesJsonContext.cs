using System.Text.Json.Serialization;
using Flare.Api.Model;

namespace Flare.Api.Json;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for
/// <see cref="Endpoints.KubernetesInventoryEndpoints"/>'s request/response DTOs - same camelCase
/// convention as <see cref="HostsJsonContext"/>. The summary/point types are picked up transitively.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(KubernetesNodeListRequest))]
[JsonSerializable(typeof(KubernetesNodeListResponse))]
[JsonSerializable(typeof(KubernetesNodeMetricsRequest))]
[JsonSerializable(typeof(KubernetesNodeMetricsResponse))]
[JsonSerializable(typeof(KubernetesPodListRequest))]
[JsonSerializable(typeof(KubernetesPodListResponse))]
[JsonSerializable(typeof(KubernetesWorkloadListRequest))]
[JsonSerializable(typeof(KubernetesWorkloadListResponse))]
[JsonSerializable(typeof(KubernetesWorkloadMetricsRequest))]
[JsonSerializable(typeof(KubernetesWorkloadMetricsResponse))]
[JsonSerializable(typeof(KubernetesNamespaceListRequest))]
[JsonSerializable(typeof(KubernetesNamespaceListResponse))]
[JsonSerializable(typeof(KubernetesVolumeListRequest))]
[JsonSerializable(typeof(KubernetesVolumeListResponse))]
[JsonSerializable(typeof(KubernetesVolumeMetricsRequest))]
[JsonSerializable(typeof(KubernetesVolumeMetricsResponse))]
public sealed partial class KubernetesJsonContext : JsonSerializerContext;
