using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Api.ResourceGraph;
using Microsoft.Extensions.Options;

namespace Flare.Api.DockerResources;

/// <summary>
/// Polls the configured socket-proxy for every Flare-labeled container, builds a
/// <see cref="ResourceGraphSnapshot"/>, and publishes it into the shared
/// <see cref="ResourceGraphSourceRegistry"/> (registered as a hosted service for
/// <see cref="ExecuteAsync"/>, wired alongside <c>KubernetesResources.KubernetesResourcePoller</c>
/// in <c>Program.cs</c> - <c>Endpoints.ResourceGraphEndpoints</c> talks to the registry, not
/// this type directly; see <see cref="SourceName"/>).
/// </summary>
/// <remarks>
/// <para>
/// Polls on an interval rather than consuming Docker's <c>/events</c> stream -
/// <c>LogTailBroadcaster</c> itself polls Redis rather than using pub/sub, and five
/// containers polled every few seconds is cheap; decoding Docker's chunked <c>/events</c>
/// JSON stream would be meaningfully more code for no real benefit at this scale. Each
/// tick lists Flare-labeled container IDs, inspects each one (the list endpoint alone
/// doesn't return structured <c>.State.Health</c>), and publishes the *whole* computed
/// snapshot - not a diff - since the graph is small enough that "send everything, every
/// tick" is simpler and cheap enough to just be correct.
/// </para>
/// <para>
/// Each tick also layers on a second, independent data source: <see cref="ProducerServiceDto"/>
/// nodes for every service that's actually sent telemetry into <c>ingest</c> recently (via
/// <see cref="ProducerOverlayBuilder"/>, ClickHouse - not Docker), with an edge into the
/// <c>"ingest"</c> role. This matters because a real producer isn't always a Docker
/// container at all - e.g. a consumer's own <c>AddProject</c> resource under Aspire's
/// dev-loop runs as a plain <c>dotnet</c> process, invisible to the Docker Engine API no
/// matter how broadly Docker-label discovery is widened. The two sources are independently
/// fallible: a ClickHouse failure here only drops the producer overlay for that tick
/// (caught separately in <see cref="PollOnceAsync"/>), never the Docker-sourced nodes/edges.
/// </para>
/// </remarks>
public sealed class DockerContainerPoller(
    DockerEngineClient dockerEngineClient,
    ILogQueryService logQueryService,
    IOptions<DockerResourcesOptions> options,
    TimeProvider timeProvider,
    ResourceGraphSourceRegistry registry,
    ILogger<DockerContainerPoller> logger) : BackgroundService
{
    /// <summary>The name this provider publishes its snapshots under - see <see cref="ResourceGraphSourceRegistry"/>.</summary>
    internal const string SourceName = "Docker";

    private static readonly ResourceGraphSnapshot NotEnabledSnapshot = new()
    {
        Available = false,
        UnavailableReason =
            "The Docker resource graph isn't enabled - set DockerResources:ProxyUrl to a " +
            "reachable docker-socket-proxy endpoint to turn it on. See docs/standalone.md " +
            "or docs/aspire-hosting.md.",
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ProxyUrl))
        {
            // Feature disabled - publish the "off" snapshot once so the registry always has
            // an entry for this provider from startup (see ResourceGraphSourceRegistry's
            // remarks), then this background service has nothing else to do. Same
            // "absent config = off" shape the rest of this repo uses (e.g.
            // Auth__IngestKeyRequired).
            registry.Publish(SourceName, NotEnabledSnapshot);
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await PollOnceAsync(stoppingToken);
                await Task.Delay(opts.PollDelay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        ResourceGraphSnapshot snapshot;
        try
        {
            var ids = await dockerEngineClient.ListFlareContainerIdsAsync(cancellationToken);
            var containers = new List<DockerContainerInspect>(ids.Count);
            foreach (var id in ids)
            {
                var inspect = await dockerEngineClient.InspectAsync(id, cancellationToken);
                if (inspect is not null)
                {
                    containers.Add(inspect);
                }
            }

            snapshot = BuildSnapshot(containers, timeProvider);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A configured-but-unreachable proxy is deliberately NOT the same as
            // "not enabled" - Available stays true (config exists), just with no nodes and
            // a reason a human can act on, so the dashboard can tell "off" apart from
            // "on but broken." See ResourceGraphSnapshot.Available's remarks. No producer
            // overlay attempted below when the Docker side itself failed - there'd be no
            // "ingest" node for its edges to point at anyway.
            logger.LogWarning(ex, "Failed to poll Docker resources via the configured socket proxy.");
            registry.Publish(SourceName, new ResourceGraphSnapshot
            {
                Available = true,
                Nodes = [],
                Edges = [],
                Producers = [],
                Provider = SourceName,
                UnavailableReason = $"Could not reach the Docker socket proxy: {ex.Message}",
            });
            return;
        }

        try
        {
            var active = await logQueryService.GetActiveServiceNamesAsync(options.Value.ProducerActivityWindow, cancellationToken);
            var (producers, producerEdges) = ProducerOverlayBuilder.Build(active);
            snapshot = snapshot with { Producers = producers, Edges = [.. snapshot.Edges, .. producerEdges] };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Independently fallible from the Docker side above - a ClickHouse hiccup
            // just means this tick's snapshot has no producer overlay, not that the whole
            // page breaks. See the class remarks.
            logger.LogWarning(ex, "Failed to query active producer services for the Resources page.");
        }

        registry.Publish(SourceName, snapshot);
    }

    /// <summary>
    /// Maps inspected containers into nodes/edges. Internal (not private) purely so
    /// <c>Flare.Api.Tests</c> can exercise the label-parsing logic directly - see
    /// <c>Flare.Api.csproj</c>'s <c>InternalsVisibleTo</c>.
    /// </summary>
    internal static ResourceGraphSnapshot BuildSnapshot(IReadOnlyList<DockerContainerInspect> containers, TimeProvider timeProvider)
    {
        var nodes = new List<ResourceNodeDto>();
        var edges = new List<ResourceEdgeDto>();

        foreach (var container in containers)
        {
            var labels = container.Config?.Labels ?? [];
            if (!labels.TryGetValue("flare.role", out var role) || string.IsNullOrWhiteSpace(role))
            {
                // Identity-labeled but role-less - shouldn't happen for anything Flare
                // itself creates, but skip rather than show a meaningless node or throw
                // away the whole snapshot over one bad label.
                continue;
            }

            nodes.Add(new ResourceNodeDto
            {
                Id = ShortId(container.Id),
                Role = role,
                Name = container.Name.TrimStart('/'),
                Image = container.Config?.Image ?? "",
                State = ParseState(container.State?.Status),
                Health = ParseHealth(container.State?.Health?.Status),
                Urls = BuildUrls(container.NetworkSettings?.Ports),
                Kind = "Container",
            });

            edges.AddRange(RelationshipLabelParser.Parse(role, labels));
        }

        return new ResourceGraphSnapshot
        {
            Available = true,
            Nodes = nodes,
            Edges = edges,
            Provider = "Docker",
            UpdatedAt = timeProvider.GetUtcNow(),
        };
    }

    internal static ResourceState ParseState(string? status) => status switch
    {
        "running" => ResourceState.Running,
        "exited" => ResourceState.Exited,
        "restarting" => ResourceState.Restarting,
        "paused" => ResourceState.Paused,
        _ => ResourceState.Unknown,
    };

    internal static ResourceHealth? ParseHealth(string? status) => status switch
    {
        "starting" => ResourceHealth.Starting,
        "healthy" => ResourceHealth.Healthy,
        "unhealthy" => ResourceHealth.Unhealthy,
        _ => null,
    };

    /// <summary>Builds <c>http://localhost:&lt;port&gt;</c> URLs from every distinct published host port - real, live, no proxying/rewriting. Docker represents an unpublished container port as a <see langword="null"/> binding list, not an empty one.</summary>
    internal static IReadOnlyList<string> BuildUrls(Dictionary<string, List<DockerPortBinding>?>? ports)
    {
        if (ports is null)
        {
            return [];
        }

        var hostPorts = new SortedSet<int>();
        foreach (var bindings in ports.Values)
        {
            if (bindings is null)
            {
                continue;
            }

            foreach (var binding in bindings)
            {
                if (binding.HostPort is not null && int.TryParse(binding.HostPort, out var port))
                {
                    hostPorts.Add(port);
                }
            }
        }

        return hostPorts.Select(p => $"http://localhost:{p}").ToArray();
    }

    /// <summary>Docker's conventional 12-char short ID, matching what <c>docker ps</c> itself displays.</summary>
    private static string ShortId(string id) => id.Length > 12 ? id[..12] : id;
}
