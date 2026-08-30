using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.ResourceGraph;

/// <summary>
/// Builds the producer-services overlay (see <see cref="ProducerServiceDto"/>) shared by
/// every topology provider - originally lived on <c>DockerResources.DockerContainerPoller</c>
/// but is entirely provider-agnostic (sourced from <see cref="ILogQueryService.GetActiveServiceNamesAsync"/>,
/// ClickHouse - never Docker or Kubernetes), so both
/// <c>DockerResources.DockerContainerPoller</c> and
/// <c>KubernetesResources.KubernetesResourcePoller</c> call this directly rather than each
/// keeping their own copy.
/// </summary>
public static class ProducerOverlayBuilder
{
    /// <summary>
    /// Maps active-service rows into <see cref="ProducerServiceDto"/> nodes plus one
    /// <c>"Producer"</c>-typed edge per producer into the <c>"ingest"</c> role. Not filtered
    /// against either provider's own Flare-managed roles - a self-referential entry is
    /// possible in principle but not expected in practice, see
    /// <see cref="ILogQueryService.GetActiveServiceNamesAsync"/>'s doc comment.
    /// </summary>
    public static (IReadOnlyList<ProducerServiceDto> Producers, IReadOnlyList<ResourceEdgeDto> Edges) Build(
        IReadOnlyList<ActiveService> activeServices)
    {
        var producers = new List<ProducerServiceDto>(activeServices.Count);
        var edges = new List<ResourceEdgeDto>(activeServices.Count);

        foreach (var service in activeServices)
        {
            var id = "service:" + service.ServiceName;
            producers.Add(new ProducerServiceDto { Id = id, ServiceName = service.ServiceName, LastSeenAt = service.LastSeenAt });
            edges.Add(new ResourceEdgeDto { SourceRole = id, TargetRole = "ingest", RelationshipType = "Producer" });
        }

        return (producers, edges);
    }
}
