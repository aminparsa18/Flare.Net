using System.Threading.Channels;
using Flare.Api.Model;

namespace Flare.Api.HostStats;

/// <summary>
/// One active <c>GET /api/resources/host/watch</c> WebSocket connection's state. Same
/// "latest snapshot only, no backlog" shape as
/// <c>DockerResources.DockerContainerSubscription</c> - see that type's remarks for why
/// capacity-1 + <see cref="BoundedChannelFullMode.DropOldest"/> is the right fit here too.
/// </summary>
public sealed class HostStatsSubscription
{
    private readonly Channel<HostStatsSnapshot> _channel = Channel.CreateBounded<HostStatsSnapshot>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = true, // only HostStatsPoller ever publishes into this.
    });

    public ChannelReader<HostStatsSnapshot> Reader => _channel.Reader;

    public void Publish(HostStatsSnapshot snapshot) => _channel.Writer.TryWrite(snapshot);

    public void Complete() => _channel.Writer.TryComplete();
}
