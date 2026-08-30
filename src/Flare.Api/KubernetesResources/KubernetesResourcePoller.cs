using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Api.ResourceGraph;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Options;

namespace Flare.Api.KubernetesResources;

/// <summary>
/// Polls the in-cluster Kubernetes API server for every Flare-labeled Pod (plus every
/// Service in the same namespace), builds a hierarchical <see cref="ResourceGraphSnapshot"/>
/// (Namespace → synthesized Deployment groups → Pods, plus Service nodes), and publishes it
/// into the shared <see cref="ResourceGraphSourceRegistry"/> - the Kubernetes counterpart to
/// <c>DockerResources.DockerContainerPoller</c>, registered as a hosted service the same way.
/// </summary>
/// <remarks>
/// <para>
/// Two scope-trimming decisions, both made to keep the required RBAC to exactly
/// <c>get</c>/<c>list</c>/<c>watch</c> on <c>pods</c>/<c>services</c> - no
/// <c>deployments</c>/<c>replicasets</c> permission at all (see
/// <c>Aspire.Hosting.Flare</c>'s <c>AddFlare</c>'s RBAC wiring): the graph never calls the
/// real Deployments API - the "Deployment" layer is synthesized by grouping Pods on their
/// <c>flare.role</c> label, not a live read of replica count/rollout status - and there's no
/// literal "Cluster" node, since a namespace-scoped <c>Role</c> (not a <c>ClusterRole</c>)
/// can't see anything outside Flare's own namespace anyway.
/// </para>
/// <para>
/// Assumes exactly one Pod per <c>flare.role</c> - true for everything <c>AddFlare</c>
/// itself creates (it never calls <c>WithReplicas</c>), and the whole edge model already
/// depends on it: <see cref="ResourceNodeDto.Role"/> is what both
/// <c>flare.relationships</c>-sourced edges and this type's own Service→Pod
/// <c>"Selects"</c> edges reference (matching <c>DockerResources.DockerContainerPoller</c>'s
/// exact convention, where a Docker deploy has the same one-container-per-role shape) - a
/// consumer manually scaling a Flare-labeled Deployment to multiple replicas would collapse
/// those Pods onto one graph node client-side (SvelteFlow's own node id is
/// <c>role</c> - see <c>src/dashboard/src/lib/resources/ResourceGraph.svelte</c>), not
/// crash, but isn't a supported/tested shape.
/// </para>
/// </remarks>
public sealed class KubernetesResourcePoller(
    ILogQueryService logQueryService,
    IOptions<KubernetesResourcesOptions> options,
    TimeProvider timeProvider,
    ResourceGraphSourceRegistry registry,
    ILogger<KubernetesResourcePoller> logger) : BackgroundService
{
    /// <summary>The name this provider publishes its snapshots under - see <see cref="ResourceGraphSourceRegistry"/>.</summary>
    internal const string SourceName = "Kubernetes";

    private static readonly ResourceGraphSnapshot NotEnabledSnapshot = new()
    {
        Available = false,
        UnavailableReason =
            "The Kubernetes resource graph isn't enabled - set KubernetesResources:Enabled to " +
            "true to turn it on (done automatically by Aspire.Hosting.Flare's " +
            "AddFlare(enableResourceGraph: true) when a Kubernetes deployment target is " +
            "registered). See docs/aspire-hosting.md.",
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            // Feature disabled - publish the "off" snapshot once so the registry always has
            // an entry for this provider from startup (see ResourceGraphSourceRegistry's
            // remarks), then this background service has nothing else to do. Same
            // "absent config = off" shape DockerContainerPoller uses.
            registry.Publish(SourceName, NotEnabledSnapshot);
            return;
        }

        Kubernetes client;
        string namespaceName;
        try
        {
            // Deliberately NOT called unless Enabled is true above - InClusterConfig() throws
            // outside a real cluster (no service-account token to read), so calling it
            // unconditionally at startup would break every non-Kubernetes deploy that happens
            // to leave this option on by mistake.
            var config = KubernetesClientConfiguration.InClusterConfig();
            namespaceName = await ResolveNamespaceAsync(config, stoppingToken);
            client = new Kubernetes(config);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Enabled but not actually running in a cluster - a static condition that won't
            // change for this process's lifetime, so publish once and stop rather than
            // retrying every PollDelay forever (contrast the "configured but unreachable"
            // case below, which genuinely can recover on a later tick).
            logger.LogWarning(ex, "KubernetesResources:Enabled is set, but this process doesn't appear to be running inside a Kubernetes cluster.");
            registry.Publish(SourceName, new ResourceGraphSnapshot
            {
                Available = true,
                Provider = SourceName,
                UnavailableReason = $"Not running inside a Kubernetes cluster: {ex.Message}",
            });
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await PollOnceAsync(client, namespaceName, stoppingToken);
                await Task.Delay(opts.PollDelay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>Standard in-cluster service-account namespace file path - stable across every Kubernetes distribution, always auto-mounted into a pod's default token projection.</summary>
    private const string ServiceAccountNamespaceFilePath = "/var/run/secrets/kubernetes.io/serviceaccount/namespace";

    /// <summary>
    /// <see cref="KubernetesClientConfiguration.InClusterConfig"/> already populates
    /// <c>Namespace</c> from <see cref="ServiceAccountNamespaceFilePath"/> in every version
    /// this has been checked against, but falls back to reading that file directly rather
    /// than assuming that always holds - the library's own path constants backing that
    /// behavior aren't public, so this can't reference them directly.
    /// </summary>
    private static async Task<string> ResolveNamespaceAsync(KubernetesClientConfiguration config, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(config.Namespace))
        {
            return config.Namespace;
        }

        return (await File.ReadAllTextAsync(ServiceAccountNamespaceFilePath, cancellationToken)).Trim();
    }

    private async Task PollOnceAsync(Kubernetes client, string namespaceName, CancellationToken cancellationToken)
    {
        ResourceGraphSnapshot snapshot;
        try
        {
            var pods = await client.ListNamespacedPodAsync(namespaceName, labelSelector: "flare.resource=true", cancellationToken: cancellationToken);
            var services = await client.ListNamespacedServiceAsync(namespaceName, cancellationToken: cancellationToken);
            snapshot = BuildSnapshot(pods.Items, services.Items, namespaceName, timeProvider);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A configured-but-unreachable API server is deliberately NOT the same as "not
            // enabled" - same reasoning as DockerContainerPoller's identical branch.
            logger.LogWarning(ex, "Failed to poll the Kubernetes API server for Flare resources.");
            registry.Publish(SourceName, new ResourceGraphSnapshot
            {
                Available = true,
                Nodes = [],
                Edges = [],
                Producers = [],
                Provider = SourceName,
                UnavailableReason = $"Could not reach the Kubernetes API server: {ex.Message}",
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
            logger.LogWarning(ex, "Failed to query active producer services for the Resources page.");
        }

        registry.Publish(SourceName, snapshot);
    }

    /// <summary>
    /// Maps Pods/Services into a hierarchical snapshot. Internal (not private) purely so
    /// <c>Flare.Api.Tests</c> can exercise it directly with hand-built <see cref="V1Pod"/>/
    /// <see cref="V1Service"/> fixtures - no fake Kubernetes API server, same pattern as
    /// <c>DockerResources.DockerContainerPoller.BuildSnapshot</c>.
    /// </summary>
    internal static ResourceGraphSnapshot BuildSnapshot(
        IList<V1Pod> pods,
        IList<V1Service> services,
        string namespaceName,
        TimeProvider timeProvider)
    {
        var nodes = new List<ResourceNodeDto>();
        var edges = new List<ResourceEdgeDto>();

        var namespaceNodeId = "namespace:" + namespaceName;
        nodes.Add(new ResourceNodeDto
        {
            Id = namespaceNodeId,
            Role = namespaceNodeId,
            Name = namespaceName,
            Image = "",
            State = ResourceState.Running,
            Kind = "Namespace",
        });

        // Group by flare.role - see the class remarks for why this synthesizes the
        // "Deployment" layer instead of reading the real Deployments API.
        var podsByRole = new Dictionary<string, List<V1Pod>>();
        foreach (var pod in pods)
        {
            if (!TryGetRole(pod, out var role))
            {
                // Identity-labeled (matched the label selector) but role-less - shouldn't
                // happen for anything Flare itself creates, but skip rather than show a
                // meaningless node or throw away the whole snapshot over one bad label. Same
                // defensive stance as DockerContainerPoller.BuildSnapshot.
                continue;
            }

            if (!podsByRole.TryGetValue(role, out var rolePods))
            {
                rolePods = [];
                podsByRole[role] = rolePods;
            }

            rolePods.Add(pod);
        }

        foreach (var (role, rolePods) in podsByRole)
        {
            var deploymentNodeId = "deployment:" + role;
            nodes.Add(new ResourceNodeDto
            {
                Id = deploymentNodeId,
                Role = deploymentNodeId,
                Name = role,
                Image = FirstContainerImage(rolePods[0]),
                State = ResourceState.Running,
                Kind = "Deployment",
                ParentId = namespaceNodeId,
            });

            foreach (var pod in rolePods)
            {
                var podName = pod.Metadata?.Name ?? role;
                nodes.Add(new ResourceNodeDto
                {
                    Id = podName,
                    Role = role,
                    Name = podName,
                    Image = FirstContainerImage(pod),
                    State = ParsePhase(pod.Status?.Phase),
                    Health = ParseHealth(pod.Status),
                    Kind = "Pod",
                    ParentId = deploymentNodeId,
                });

                if (pod.Metadata?.Labels is { } labels)
                {
                    edges.AddRange(RelationshipLabelParser.Parse(role, labels));
                }
            }
        }

        foreach (var service in services)
        {
            var selector = service.Spec?.Selector;
            var serviceName = service.Metadata?.Name;
            if (selector is not { Count: > 0 } || serviceName is null)
            {
                // No selector (e.g. a headless/ExternalName Service) or unnamed - nothing to
                // draw an edge to and nothing to key a node on.
                continue;
            }

            // Namespaced "k8s-service:" prefix, distinct from ProducerServiceDto.Id's own
            // "service:" prefix (see ProducerServiceDto.Id's remarks) - a real Kubernetes
            // Service happening to share a name with an observed producer service must never
            // collide on this graph.
            var serviceNodeId = "k8s-service:" + serviceName;
            var selectedAnyFlareRolePod = false;
            foreach (var pod in pods)
            {
                if (!TryGetRole(pod, out var role) || !MatchesSelector(pod.Metadata?.Labels, selector))
                {
                    continue;
                }

                selectedAnyFlareRolePod = true;
                edges.Add(new ResourceEdgeDto { SourceRole = serviceNodeId, TargetRole = role, RelationshipType = "Selects" });
            }

            if (!selectedAnyFlareRolePod)
            {
                // Doesn't select any Flare-labeled Pod - a consumer's own unrelated Service in
                // the same namespace, not interesting on this graph.
                continue;
            }

            nodes.Add(new ResourceNodeDto
            {
                Id = serviceNodeId,
                Role = serviceNodeId,
                Name = serviceName,
                Image = "",
                State = ResourceState.Running,
                Kind = "Service",
                ParentId = namespaceNodeId,
            });
        }

        return new ResourceGraphSnapshot
        {
            Available = true,
            Nodes = nodes,
            Edges = edges,
            Provider = SourceName,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
    }

    private static bool TryGetRole(V1Pod pod, out string role)
    {
        if (pod.Metadata?.Labels is { } labels && labels.TryGetValue("flare.role", out var value) && !string.IsNullOrWhiteSpace(value))
        {
            role = value;
            return true;
        }

        role = "";
        return false;
    }

    /// <summary>Whether every key/value pair in <paramref name="selector"/> is present in <paramref name="podLabels"/> - the same match rule the Kubernetes Service controller itself uses.</summary>
    internal static bool MatchesSelector(IDictionary<string, string>? podLabels, IDictionary<string, string> selector)
    {
        if (podLabels is null)
        {
            return false;
        }

        foreach (var (key, value) in selector)
        {
            if (!podLabels.TryGetValue(key, out var podValue) || podValue != value)
            {
                return false;
            }
        }

        return true;
    }

    private static string FirstContainerImage(V1Pod pod) => pod.Spec?.Containers?.Count > 0 ? pod.Spec.Containers[0].Image ?? "" : "";

    /// <summary>Maps a Pod's <c>.status.phase</c> to <see cref="ResourceState"/> - a coarser mapping than Docker's, since Kubernetes has no direct equivalent of "restarting"/"paused" at the phase level (see the class remarks for the deliberate fidelity trade-off).</summary>
    internal static ResourceState ParsePhase(string? phase) => phase switch
    {
        "Running" => ResourceState.Running,
        "Succeeded" or "Failed" => ResourceState.Exited,
        _ => ResourceState.Unknown, // "Pending", "Unknown", or absent.
    };

    /// <summary>
    /// Approximates Docker's explicit-<c>HEALTHCHECK</c>-derived <see cref="ResourceHealth"/>
    /// from Kubernetes' readiness signal, since Kubernetes has no single field that maps
    /// directly: no container statuses reported yet reads as <see langword="null"/> (matching
    /// Docker's "no healthcheck configured" meaning), a still-<c>Pending</c> pod reads as
    /// <see cref="ResourceHealth.Starting"/>, and otherwise every reported container being
    /// <see cref="V1ContainerStatus.Ready"/> is <see cref="ResourceHealth.Healthy"/> vs.
    /// <see cref="ResourceHealth.Unhealthy"/>.
    /// </summary>
    internal static ResourceHealth? ParseHealth(V1PodStatus? status)
    {
        var containerStatuses = status?.ContainerStatuses;
        if (containerStatuses is not { Count: > 0 })
        {
            return null;
        }

        if (status!.Phase == "Pending")
        {
            return ResourceHealth.Starting;
        }

        foreach (var containerStatus in containerStatuses)
        {
            if (!containerStatus.Ready)
            {
                return ResourceHealth.Unhealthy;
            }
        }

        return ResourceHealth.Healthy;
    }
}
