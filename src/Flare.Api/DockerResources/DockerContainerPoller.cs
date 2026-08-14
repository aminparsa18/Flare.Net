using System.Collections.Concurrent;
using Flare.Api.Model;
using Flare.Api.Query;
using Microsoft.Extensions.Options;

namespace Flare.Api.DockerResources;

/// <summary>
/// The single shared reader behind every <c>GET /api/resources/watch</c> connection and
/// <c>GET /api/resources/snapshot</c> request: polls the configured socket-proxy for every
/// Flare-labeled container, builds a <see cref="ResourceGraphSnapshot"/>, and fans it out
/// to every subscriber - the same "one instance, two roles" pattern as
/// <c>LiveTail.LogTailBroadcaster</c> (registered as both a singleton, so
/// <c>Endpoints.ResourceGraphEndpoints</c> can call <see cref="Subscribe"/>/
/// <see cref="Unsubscribe"/> and read <see cref="CurrentSnapshot"/>, and a hosted service,
/// for <see cref="ExecuteAsync"/> - wired in <c>Program.cs</c>).
/// </summary>
/// <remarks>
/// <para>
/// Polls on an interval rather than consuming Docker's <c>/events</c> stream -
/// <c>LogTailBroadcaster</c> itself polls Redis rather than using pub/sub, and five
/// containers polled every few seconds is cheap; decoding Docker's chunked <c>/events</c>
/// JSON stream would be meaningfully more code for no real benefit at this scale. Each
/// tick lists Flare-labeled container IDs, inspects each one (the list endpoint alone
/// doesn't return structured <c>.State.Health</c>), and broadcasts the *whole* computed
/// snapshot - not a diff - since the graph is small enough that "send everything, every
/// tick" is simpler and cheap enough to just be correct.
/// </para>
/// <para>
/// Each tick also layers on a second, independent data source: <see cref="ProducerServiceDto"/>
/// nodes for every service that's actually sent telemetry into <c>ingest</c> recently
/// (via <see cref="ILogQueryService.GetActiveServiceNamesAsync"/>, ClickHouse - not
/// Docker), with an edge into the <c>"ingest"</c> role. This matters because a real
/// producer isn't always a Docker container at all - e.g. a consumer's own
/// <c>AddProject</c> resource under Aspire's dev-loop runs as a plain <c>dotnet</c>
/// process, invisible to the Docker Engine API no matter how broadly Docker-label
/// discovery is widened. The two sources are independently fallible: a ClickHouse
/// failure here only drops the producer overlay for that tick (caught separately in
/// <see cref="PollOnceAsync"/>), never the Docker-sourced nodes/edges.
/// </para>
/// </remarks>
public sealed class DockerContainerPoller(
    DockerEngineClient dockerEngineClient,
    ILogQueryService logQueryService,
    IOptions<DockerResourcesOptions> options,
    TimeProvider timeProvider,
    ILogger<DockerContainerPoller> logger) : BackgroundService
{
    private static readonly ResourceGraphSnapshot NotEnabledSnapshot = new()
    {
        Available = false,
        UnavailableReason =
            "The Docker resource graph isn't enabled - set DockerResources:ProxyUrl to a " +
            "reachable docker-socket-proxy endpoint to turn it on. See docs/standalone.md " +
            "or docs/aspire-hosting.md.",
    };

    private readonly ConcurrentDictionary<DockerContainerSubscription, byte> _subscriptions = new();
    private ResourceGraphSnapshot _currentSnapshot = NotEnabledSnapshot;

    /// <summary>The most recently published snapshot - what <c>GET /api/resources/snapshot</c> returns, with no live Docker call on the request path.</summary>
    public ResourceGraphSnapshot CurrentSnapshot
    {
        get => Volatile.Read(ref _currentSnapshot);
        private set => Volatile.Write(ref _currentSnapshot, value);
    }

    public DockerContainerSubscription Subscribe()
    {
        var subscription = new DockerContainerSubscription();
        _subscriptions[subscription] = 0;
        // Don't make a fresh connection wait up to a full PollDelay for its first snapshot.
        subscription.Publish(CurrentSnapshot);
        return subscription;
    }

    public void Unsubscribe(DockerContainerSubscription subscription)
    {
        _subscriptions.TryRemove(subscription, out _);
        subscription.Complete();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ProxyUrl))
        {
            // Feature disabled - CurrentSnapshot stays NotEnabledSnapshot forever, and this
            // background service has nothing else to do. Same "absent config = off" shape
            // the rest of this repo uses (e.g. Auth__IngestKeyRequired).
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
            Publish(CurrentSnapshot with
            {
                Available = true,
                Nodes = [],
                Edges = [],
                Producers = [],
                UnavailableReason = $"Could not reach the Docker socket proxy: {ex.Message}",
            });
            return;
        }

        try
        {
            var active = await logQueryService.GetActiveServiceNamesAsync(options.Value.ProducerActivityWindow, cancellationToken);
            var (producers, producerEdges) = BuildProducerOverlay(active);
            snapshot = snapshot with { Producers = producers, Edges = [.. snapshot.Edges, .. producerEdges] };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Independently fallible from the Docker side above - a ClickHouse hiccup
            // just means this tick's snapshot has no producer overlay, not that the whole
            // page breaks. See the class remarks.
            logger.LogWarning(ex, "Failed to query active producer services for the Resources page.");
        }

        Publish(snapshot);
    }

    private void Publish(ResourceGraphSnapshot snapshot)
    {
        CurrentSnapshot = snapshot;
        foreach (var subscription in _subscriptions.Keys)
        {
            subscription.Publish(snapshot);
        }
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
            });

            edges.AddRange(ParseRelationships(role, labels));
        }

        return new ResourceGraphSnapshot
        {
            Available = true,
            Nodes = nodes,
            Edges = edges,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
    }

    /// <summary>
    /// Maps active-service rows into <see cref="ProducerServiceDto"/> nodes plus one
    /// <c>"Producer"</c>-typed edge per producer into the <c>"ingest"</c> role. Internal
    /// (not private) for the same reason <see cref="BuildSnapshot"/> is - direct unit
    /// testing without a fake <see cref="ILogQueryService"/>. Not filtered against
    /// Flare's own Docker roles - see the class remarks and <c>ILogQueryService.GetActiveServiceNamesAsync</c>'s
    /// doc comment for why a self-referential entry is possible in principle but not
    /// expected in practice.
    /// </summary>
    internal static (IReadOnlyList<ProducerServiceDto> Producers, IReadOnlyList<ResourceEdgeDto> Edges) BuildProducerOverlay(
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

    /// <summary>Parses a <c>flare.relationships</c> label value (e.g. <c>"clickhouse:Reference,redis:Reference"</c>) into edges sourced from <paramref name="sourceRole"/>. Malformed entries are skipped individually, not fatal to the rest of the label.</summary>
    private static IEnumerable<ResourceEdgeDto> ParseRelationships(string sourceRole, Dictionary<string, string> labels)
    {
        if (!labels.TryGetValue("flare.relationships", out var relationships) || string.IsNullOrWhiteSpace(relationships))
        {
            yield break;
        }

        foreach (var entry in relationships.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(':', 2);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                continue;
            }

            yield return new ResourceEdgeDto { SourceRole = sourceRole, TargetRole = parts[0], RelationshipType = parts[1] };
        }
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
