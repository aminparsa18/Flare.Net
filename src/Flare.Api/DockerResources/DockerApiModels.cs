namespace Flare.Api.DockerResources;

/// <summary>
/// The subset of a Docker Engine API <c>GET /containers/json</c> list entry
/// <see cref="DockerEngineClient"/> actually needs - just enough to enumerate container
/// IDs to inspect. Field names match Docker's own JSON exactly (PascalCase, no naming
/// policy) - see <see cref="DockerApiJsonContext"/>'s remarks for why this uses different
/// JSON conventions than Flare's own outbound DTOs.
/// </summary>
internal sealed record DockerContainerSummary
{
    public string Id { get; init; } = "";
}

/// <summary>The subset of a Docker Engine API <c>GET /containers/{id}/json</c> inspect response <see cref="DockerEngineClient"/> actually needs.</summary>
internal sealed record DockerContainerInspect
{
    public string Id { get; init; } = "";

    /// <summary>Docker always prefixes this with a leading <c>/</c> - stripped by <c>DockerContainerPoller</c>, not here.</summary>
    public string Name { get; init; } = "";

    public DockerContainerConfig? Config { get; init; }

    public DockerContainerState? State { get; init; }

    public DockerNetworkSettings? NetworkSettings { get; init; }
}

internal sealed record DockerContainerConfig
{
    public string? Image { get; init; }

    public Dictionary<string, string>? Labels { get; init; }
}

/// <summary><see cref="Status"/> (running/exited/restarting/paused/...) is always populated; <see cref="Health"/> only when a <c>HEALTHCHECK</c> is configured on the image.</summary>
internal sealed record DockerContainerState
{
    public string? Status { get; init; }

    public DockerContainerHealth? Health { get; init; }
}

internal sealed record DockerContainerHealth
{
    /// <summary><c>"starting"</c>/<c>"healthy"</c>/<c>"unhealthy"</c>.</summary>
    public string? Status { get; init; }
}

/// <summary><see cref="Ports"/> is keyed by <c>"&lt;containerPort&gt;/&lt;proto&gt;"</c> (e.g. <c>"8123/tcp"</c>); an unpublished port's value is <see langword="null"/>, not an empty array.</summary>
internal sealed record DockerNetworkSettings
{
    public Dictionary<string, List<DockerPortBinding>?>? Ports { get; init; }
}

internal sealed record DockerPortBinding
{
    public string? HostIp { get; init; }

    public string? HostPort { get; init; }
}
