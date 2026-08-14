namespace Flare.Api.DockerResources;

/// <summary>
/// Tuning knobs for the Docker-driven Resources page (<c>GET /api/resources/snapshot</c> /
/// <c>GET /api/resources/watch</c>). Bound from the <c>DockerResources</c> configuration
/// section.
/// </summary>
/// <remarks>
/// See <see cref="DockerContainerPoller"/> - the single background reader that polls the
/// configured socket-proxy and fans the computed graph out to every connected WebSocket
/// subscriber. <see cref="ProxyUrl"/> unset (the default) is this feature's entire "off"
/// switch - deliberately the same "absent config = off" pattern the rest of this repo
/// uses (e.g. <c>Auth__IngestKeyRequired</c>), not a separate boolean flag that could drift
/// out of sync with whether a URL is actually configured.
/// </remarks>
public sealed class DockerResourcesOptions
{
    public const string SectionName = "DockerResources";

    /// <summary>
    /// Base URL of a <c>tecnativa/docker-socket-proxy</c> (or API-compatible) instance,
    /// e.g. <c>http://docker-proxy:2375</c>. <see langword="null"/>/empty (the default)
    /// disables this feature entirely - Flare.Api never talks to
    /// <c>/var/run/docker.sock</c> directly, only ever through this proxy. See
    /// <c>docs/standalone.md</c>/<c>docs/aspire-hosting.md</c> for why this is opt-in and
    /// what the proxy's own scoping restricts.
    /// </summary>
    public string? ProxyUrl { get; set; }

    /// <summary>
    /// How often to re-list and re-inspect Flare-labeled containers. Five containers
    /// polled on this interval is cheap - see <see cref="DockerContainerPoller"/>'s remarks
    /// for why this polls instead of consuming Docker's <c>/events</c> stream.
    /// </summary>
    public TimeSpan PollDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How far back to look for "recently active" producer services (see
    /// <see cref="DockerContainerPoller"/>'s producer-overlay remarks) - a service with no
    /// log event inside this window drops off the graph on the next poll tick, same
    /// "live topology, not history" spirit as everything else on this page.
    /// </summary>
    public TimeSpan ProducerActivityWindow { get; set; } = TimeSpan.FromMinutes(5);
}
