using System.Collections.Concurrent;
using Flare.Api.Model;

namespace Flare.Api.ResourceGraph;

/// <summary>
/// The single shared surface behind every <c>GET /api/resources/watch</c> connection and
/// <c>GET /api/resources/snapshot</c> request - <c>Endpoints.ResourceGraphEndpoints</c>
/// depends on this, not on either provider directly. Reconciles the (at most two, in
/// practice exactly one) topology providers - <c>DockerResources.DockerContainerPoller</c>
/// and <c>KubernetesResources.KubernetesResourcePoller</c> - into one
/// <see cref="ResourceGraphSnapshot"/>/subscription stream, so the Resources page keeps a
/// single entry point regardless of which backend an <c>AddFlare</c> AppHost's deployment
/// target wired up. Same "singleton, poller(s) publish into it, endpoints read/subscribe
/// from it" shape <c>DockerContainerPoller</c> used to own directly before this type
/// existed.
/// </summary>
/// <remarks>
/// Each provider publishes under its own fixed name (<c>"Docker"</c>/<c>"Kubernetes"</c>)
/// unconditionally from its very first tick - including its own "not configured" snapshot
/// when it's off (see each poller's <c>ExecuteAsync</c>) - so <see cref="_bySource"/> always
/// has an entry per registered provider from startup, not just once one becomes live.
/// <see cref="ComputeCurrent"/> then picks, in order: a provider that's actually reporting
/// nodes, else a provider that's on but broken (<c>Available</c> true, no nodes,
/// <c>UnavailableReason</c> set), else whichever "not configured" snapshot happened to
/// publish first. Only one provider is ever expected to be genuinely configured per deploy
/// - this ordering exists mainly to make the "both happen to be configured" edge case behave
/// sensibly rather than to arbitrate a real steady-state conflict.
/// </remarks>
public sealed class ResourceGraphSourceRegistry
{
    private readonly ConcurrentDictionary<string, ResourceGraphSnapshot> _bySource = new();
    private readonly ConcurrentDictionary<ResourceGraphSubscription, byte> _subscriptions = new();

    /// <summary>What <c>GET /api/resources/snapshot</c> returns - recomputed from the latest per-provider snapshots, not cached, since there are at most two providers to scan.</summary>
    public ResourceGraphSnapshot CurrentSnapshot => ComputeCurrent();

    public ResourceGraphSubscription Subscribe()
    {
        var subscription = new ResourceGraphSubscription();
        _subscriptions[subscription] = 0;
        // Don't make a fresh connection wait up to a full poll delay for its first snapshot.
        subscription.Publish(CurrentSnapshot);
        return subscription;
    }

    public void Unsubscribe(ResourceGraphSubscription subscription)
    {
        _subscriptions.TryRemove(subscription, out _);
        subscription.Complete();
    }

    /// <summary>Called by a provider poller on every tick (including its very first, "not configured" one) - see the class remarks.</summary>
    public void Publish(string sourceName, ResourceGraphSnapshot snapshot)
    {
        _bySource[sourceName] = snapshot;
        var current = ComputeCurrent();
        foreach (var subscription in _subscriptions.Keys)
        {
            subscription.Publish(current);
        }
    }

    private ResourceGraphSnapshot ComputeCurrent()
    {
        var snapshots = _bySource.Values;

        foreach (var snapshot in snapshots)
        {
            if (snapshot.Available && snapshot.Nodes.Count > 0)
            {
                return snapshot;
            }
        }

        foreach (var snapshot in snapshots)
        {
            if (snapshot.Available)
            {
                return snapshot;
            }
        }

        foreach (var snapshot in snapshots)
        {
            return snapshot;
        }

        // No provider has published yet at all (registered but ExecuteAsync hasn't run its
        // first iteration) - shouldn't be observable in practice since both pollers publish
        // synchronously before their first Task.Delay, but a safe default regardless.
        return new ResourceGraphSnapshot { Available = false };
    }
}
