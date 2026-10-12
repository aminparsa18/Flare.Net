using System.Threading.Channels;
using Flare.Api.Model;

namespace Flare.Api.ResourceGraph;

/// <summary>
/// One active <c>GET /api/resources/watch</c> WebSocket connection's state. Provider-agnostic
/// (used identically by <c>DockerResources.DockerContainerPoller</c> and
/// <c>KubernetesResources.KubernetesResourcePoller</c> - see <see cref="ResourceGraphSourceRegistry"/>
/// for how the two are reconciled into one subscription surface). Simpler than
/// <c>LiveTail.LogTailSubscription</c>: there's no client-sent filter/pause protocol (the
/// resources graph isn't filtered), and the channel only ever needs to hold the single
/// *latest* snapshot rather than every message published since the last read - see the
/// channel options below.
/// </summary>
public sealed class ResourceGraphSubscription
{
    private readonly Channel<ResourceGraphSnapshot> _channel = Channel.CreateBounded<ResourceGraphSnapshot>(new BoundedChannelOptions(1)
    {
        // Unlike LogTailSubscription's DropWrite (every event matters; drops are counted
        // and reported to the client), a slow reader here should just catch up to wherever
        // the poller currently is - an intermediate snapshot has zero value once a newer
        // one exists. DropOldest + capacity 1 collapses any backlog down to "always the
        // freshest known state" with no drop-counting/reporting needed.
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = true, // only ResourceGraphSourceRegistry ever publishes into this.
    });

    /// <summary>
    /// The project allow-list of whoever opened this connection, captured at construction (ADR-0123) because
    /// <see cref="Query.ServiceScope.Current"/> is not set on the poller thread that calls <see cref="Publish"/>. Null = unrestricted.
    /// </summary>
    public IReadOnlyList<string>? AllowedServices { get; } = Query.ServiceScope.Current;

    public ChannelReader<ResourceGraphSnapshot> Reader => _channel.Reader;

    public void Publish(ResourceGraphSnapshot snapshot) =>
        _channel.Writer.TryWrite(ResourceGraphScopeFilter.Apply(snapshot, AllowedServices));

    public void Complete() => _channel.Writer.TryComplete();
}
