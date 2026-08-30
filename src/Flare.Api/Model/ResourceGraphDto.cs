namespace Flare.Api.Model;

/// <summary>
/// A Flare-managed container's coarse lifecycle state, mapped from Docker's own
/// <c>.State.Status</c> inspect field (<c>running</c>/<c>exited</c>/<c>restarting</c>/
/// <c>paused</c>/<c>created</c>/<c>dead</c>) - always populated regardless of whether the
/// container has a healthcheck configured. <see cref="Unknown"/> covers any Docker status
/// string this enum doesn't otherwise name (e.g. <c>created</c>/<c>dead</c>, or a poll that
/// raced a container's removal) rather than throwing on deserialize.
/// </summary>
public enum ResourceState
{
    Unknown,
    Running,
    Exited,
    Restarting,
    Paused,
}

/// <summary>
/// A Flare-managed container's Docker-native healthcheck status
/// (<c>.State.Health.Status</c>), a refinement of <see cref="ResourceState"/> that's only
/// meaningful when the container actually has a <c>HEALTHCHECK</c> - see
/// <see cref="ResourceNodeDto.Health"/> for when this is <see langword="null"/> instead.
/// </summary>
public enum ResourceHealth
{
    Starting,
    Healthy,
    Unhealthy,
}

/// <summary>
/// One Flare-managed container - <see cref="Role"/> is the stable cross-orchestrator
/// identity (the same <c>flare.role</c> label value under both docker-compose and a
/// consumer's <c>AddFlare()</c> AppHost, even though the actual container name/ID differs
/// between the two - see <c>docs/prompts/docker-resources-graph-prompt.md</c>) and is what
/// <see cref="ResourceEdgeDto"/> references, not <see cref="Id"/>. Also doubles as the
/// node type for every Kubernetes-provider object (see <c>KubernetesResources.KubernetesResourcePoller</c>)
/// - <see cref="Kind"/>/<see cref="ParentId"/> exist purely for that provider; Docker nodes
/// always have <c>Kind: "Container"</c> and a <see langword="null"/> <see cref="ParentId"/>
/// (Docker's graph is flat).
/// </summary>
public sealed record ResourceNodeDto
{
    /// <summary>
    /// Docker: the container's own short ID, stable for its lifetime but changing across a
    /// recreate. Kubernetes: the object's stable name (Pod/Service name, or a synthetic id
    /// for the Namespace root / synthesized Deployment-group nodes) - see
    /// <c>KubernetesResources.KubernetesResourcePoller</c>'s remarks.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>This container's <c>flare.role</c> label value (e.g. <c>"clickhouse"</c>, <c>"ingest"</c>) - see the type doc comment.</summary>
    public required string Role { get; init; }

    /// <summary>Docker's container name (the leading <c>/</c> Docker's inspect API always prefixes it with, stripped), or the Kubernetes object's own name.</summary>
    public required string Name { get; init; }

    public required string Image { get; init; }

    public required ResourceState State { get; init; }

    /// <summary><see langword="null"/> when this container has no Docker <c>HEALTHCHECK</c> configured (e.g. the dashboard image, before this feature added one) - distinct from any <see cref="ResourceHealth"/> value, which all imply a healthcheck exists.</summary>
    public ResourceHealth? Health { get; init; }

    /// <summary>
    /// Real, live URLs built from this container's published host ports
    /// (<c>http://localhost:&lt;port&gt;</c>) - not proxied/rewritten. Empty when the
    /// container publishes no host ports (nothing external needs to reach it directly).
    /// </summary>
    public IReadOnlyList<string> Urls { get; init; } = [];

    /// <summary>
    /// <c>"Container"</c> for every Docker-provider node (the only kind that provider ever
    /// emits). The Kubernetes provider emits <c>"Namespace"</c>/<c>"Deployment"</c>/
    /// <c>"Pod"</c>/<c>"Service"</c> - not a closed enum for the same reason
    /// <see cref="ResourceEdgeDto.RelationshipType"/> isn't, so a new kind never needs a
    /// Flare.Api code change to show up on the wire.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// This node's parent in the Kubernetes provider's hierarchy (a Pod's parent is its
    /// synthesized Deployment-group node, whose own parent is the Namespace root) -
    /// <see langword="null"/> for the Namespace root itself and for every Docker node
    /// (Docker's graph is flat; see the type doc comment). Purely a rendering hint - not
    /// referenced by <see cref="ResourceEdgeDto"/>, which stays keyed by <see cref="Role"/>
    /// even for Kubernetes nodes.
    /// </summary>
    public string? ParentId { get; init; }
}

/// <summary>
/// One relationship edge between two graph nodes. Two distinct sources feed this list
/// (see <c>DockerResources.DockerContainerPoller</c>'s remarks): Flare-authored
/// <c>flare.relationships</c> container labels (e.g. <c>"clickhouse:Reference,redis:Reference"</c>
/// on <c>ingest</c> yields two edges with <see cref="SourceRole"/> <c>"ingest"</c> -
/// not derived from Docker Compose's <c>depends_on</c> or Aspire's own relationship
/// graph, neither of which is readable from the Docker Engine API at runtime, see
/// <c>docs/prompts/docker-resources-graph-prompt.md</c>), and the producer-services
/// overlay (a <see cref="ProducerServiceDto.Id"/> referencing <c>"ingest"</c>, for every
/// service observed sending it telemetry).
/// </summary>
public sealed record ResourceEdgeDto
{
    /// <summary>Despite the name, any graph node id - a Docker <c>flare.role</c> value (<see cref="ResourceNodeDto.Role"/>) or a producer's <see cref="ProducerServiceDto.Id"/>. Kept as "Role" rather than renamed to "Id" since every Docker-sourced edge (the common case) is genuinely role-to-role.</summary>
    public required string SourceRole { get; init; }

    /// <summary>See <see cref="SourceRole"/>'s remarks - same "any node id" meaning.</summary>
    public required string TargetRole { get; init; }

    /// <summary>Free-form relationship type (e.g. <c>"Reference"</c> from a label, or <c>"Producer"</c> from the telemetry overlay) - not a closed enum, since new relationship kinds shouldn't need a Flare.Api code change to show up.</summary>
    public required string RelationshipType { get; init; }
}

/// <summary>
/// One service observed sending telemetry into <c>ingest</c> recently - sourced from
/// ClickHouse (<see cref="Query.ILogQueryService.GetActiveServiceNamesAsync"/>), not
/// Docker. Distinct from <see cref="ResourceNodeDto"/> on purpose: a producer isn't a
/// container Flare manages (it may not be a container at all - see
/// <c>DockerResources.DockerContainerPoller</c>'s remarks for the motivating case, a
/// consumer's own <c>AddProject</c> resource with no Docker footprint whatsoever), so it
/// has no image/state/health/urls to report.
/// </summary>
public sealed record ProducerServiceDto
{
    /// <summary><c>"service:" + </c><see cref="ServiceName"/> - namespaced so a producer can never collide with a Docker <see cref="ResourceNodeDto.Role"/> value (e.g. a producer literally named <c>"api"</c>).</summary>
    public required string Id { get; init; }

    public required string ServiceName { get; init; }

    public required DateTimeOffset LastSeenAt { get; init; }
}

/// <summary>
/// The full payload behind both <c>GET /api/resources/snapshot</c> and every message
/// pushed over <c>GET /api/resources/watch</c> - see <see cref="DockerResources.DockerContainerPoller"/>
/// for how it's built and broadcast.
/// </summary>
public sealed record ResourceGraphSnapshot
{
    /// <summary>
    /// <see langword="false"/> means the Docker resource graph isn't enabled (no
    /// <c>DockerResources:ProxyUrl</c> configured) - a deliberate, config-driven "off"
    /// state, not an error. A configured-but-unreachable proxy is a different state: this
    /// stays <see langword="true"/>, <see cref="Nodes"/>/<see cref="Edges"/> are empty, and
    /// <see cref="UnavailableReason"/> describes the connection failure - see this feature's
    /// design notes for why "not enabled" and "enabled but broken" need to read differently
    /// in the dashboard.
    /// </summary>
    public required bool Available { get; init; }

    /// <summary>Human-readable explanation, set whenever <see cref="Nodes"/> is empty for a reason worth surfacing (not enabled, or a poll failure) - <see langword="null"/> once real data is showing.</summary>
    public string? UnavailableReason { get; init; }

    public IReadOnlyList<ResourceNodeDto> Nodes { get; init; } = [];

    public IReadOnlyList<ResourceEdgeDto> Edges { get; init; } = [];

    /// <summary>Producer-services overlay (see <see cref="ProducerServiceDto"/>) - always empty when <see cref="Available"/> is <see langword="false"/>, and best-effort even when it's <see langword="true"/> (a ClickHouse query failure here doesn't take down <see cref="Nodes"/>/<see cref="Edges"/> - see <c>DockerResources.DockerContainerPoller</c>'s remarks).</summary>
    public IReadOnlyList<ProducerServiceDto> Producers { get; init; } = [];

    /// <summary><see langword="null"/> until the first successful poll completes.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Which topology provider produced this snapshot - <c>"Docker"</c> or
    /// <c>"Kubernetes"</c>, or <see langword="null"/> when neither is configured (same
    /// state as <see cref="Available"/> being <see langword="false"/>). Only one provider
    /// is ever configured per deploy in practice (an <c>AddFlare</c> AppHost registers
    /// either a Docker Compose or a Kubernetes environment, not both), so this is mainly a
    /// rendering hint for the dashboard to pick a flat vs. hierarchical layout - see
    /// <c>ResourceGraph.ResourceGraphSourceRegistry</c> for how the two providers'
    /// snapshots are reconciled into this single value.
    /// </summary>
    public string? Provider { get; init; }
}
