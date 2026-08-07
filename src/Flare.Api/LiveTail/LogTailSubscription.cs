using System.Threading.Channels;
using Flare.Api.Model;

namespace Flare.Api.LiveTail;

/// <summary>One active <c>GET /api/logs/tail</c> WebSocket connection's state.</summary>
/// <remarks>
/// The channel carries the full <see cref="LogTailServerMessage"/> envelope, not just
/// matched events - both <see cref="LogTailBroadcaster"/> (publishing matched events) and
/// this connection's own receive loop (publishing <see cref="LogTailServerMessageType.Error"/>
/// replies to malformed client messages) write into it, so <c>System.Net.WebSockets.WebSocket</c>
/// only ever has one in-flight send at a time - the endpoint's send loop is the sole
/// reader/sender. <see cref="LogTailServerMessageType.Dropped"/> messages are synthesized
/// by that same send loop from <see cref="TakeDroppedCount"/> rather than written here,
/// since a full channel can't also enqueue a message announcing that fact.
/// </remarks>
public sealed class LogTailSubscription
{
    private readonly Channel<LogTailServerMessage> _channel;
    private int _droppedSinceLastReport;

    public LogTailSubscription(int channelCapacity)
    {
        _channel = Channel.CreateBounded<LogTailServerMessage>(new BoundedChannelOptions(channelCapacity)
        {
            // A full channel means this connection's send loop can't keep up - fail the
            // write and count it (see TryPublishEvent) rather than block the shared
            // broadcaster loop for every other subscriber, or silently evict via
            // DropOldest (which can't report back that anything was lost).
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false, // the broadcaster loop and this connection's own receive loop (error replies) both write.
        });
    }

    /// <summary>The current filter, applied by <see cref="LogTailBroadcaster"/> at fan-out time. Replaced wholesale on every <see cref="LogTailClientMessageType.Subscribe"/> message.</summary>
    public LogFilter Filter { get; set; } = new();

    /// <summary>
    /// While <see langword="true"/>, <see cref="TryPublishEvent"/> is a no-op - events
    /// published while paused are dropped, not queued, and don't count toward
    /// <see cref="TakeDroppedCount"/> (that counter is specifically for backpressure, not
    /// a deliberate pause).
    /// </summary>
    public bool Paused { get; set; }

    public ChannelReader<LogTailServerMessage> Reader => _channel.Reader;

    /// <summary>
    /// Enqueues a matching event unless <see cref="Paused"/>; if the channel is full,
    /// increments the dropped-event counter instead of blocking the caller.
    /// </summary>
    public void TryPublishEvent(LogEventDto logEvent)
    {
        if (Paused)
        {
            return;
        }

        if (!_channel.Writer.TryWrite(new LogTailServerMessage { Type = LogTailServerMessageType.Event, Event = logEvent }))
        {
            Interlocked.Increment(ref _droppedSinceLastReport);
        }
    }

    /// <summary>
    /// Best-effort enqueue of a protocol error reply to a malformed client message - never
    /// counted against <see cref="TakeDroppedCount"/> (that's reserved for backpressure on
    /// the actual event feed); if the channel happens to be full, the error is simply not
    /// delivered rather than displacing a queued event.
    /// </summary>
    public void TryPublishError(string message) =>
        _channel.Writer.TryWrite(new LogTailServerMessage { Type = LogTailServerMessageType.Error, Error = message });

    /// <summary>Atomically reads and resets the dropped-since-last-report counter, for the send loop to fold into a <see cref="LogTailServerMessageType.Dropped"/> message.</summary>
    public int TakeDroppedCount() => Interlocked.Exchange(ref _droppedSinceLastReport, 0);

    public void Complete() => _channel.Writer.TryComplete();
}
