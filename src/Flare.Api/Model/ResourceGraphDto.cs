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
/// <see cref="ResourceEdgeDto"/> references, not <see cref="Id"/>.
/// </summary>
public sealed record ResourceNodeDto
{
    /// <summary>Docker's own short container ID - stable for this container's lifetime, but changes across a recreate (e.g. <c>docker compose up --force-recreate</c>).</summary>
    public required string Id { get; init; }

    /// <summary>This container's <c>flare.role</c> label value (e.g. <c>"clickhouse"</c>, <c>"ingest"</c>) - see the type doc comment.</summary>
    public required string Role { get; init; }

    /// <summary>Docker's container name, with the leading <c>/</c> Docker's inspect API always prefixes it with stripped.</summary>
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
}

/// <summary>
/// One Flare-authored relationship between two roles, parsed from the referencing
/// container's <c>flare.relationships</c> label (e.g. <c>"clickhouse:Reference,redis:Reference"</c>
/// on <c>ingest</c> yields two edges with <see cref="SourceRole"/> <c>"ingest"</c>). Not
/// derived from Docker Compose's <c>depends_on</c> or Aspire's own relationship graph -
/// neither is readable from the Docker Engine API at runtime, see
/// <c>docs/prompts/docker-resources-graph-prompt.md</c>.
/// </summary>
public sealed record ResourceEdgeDto
{
    public required string SourceRole { get; init; }

    public required string TargetRole { get; init; }

    /// <summary>Free-form relationship type from the label value (e.g. <c>"Reference"</c>) - not a closed enum, since new relationship kinds shouldn't need a Flare.Api code change to show up.</summary>
    public required string RelationshipType { get; init; }
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

    /// <summary><see langword="null"/> until the first successful poll completes.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
