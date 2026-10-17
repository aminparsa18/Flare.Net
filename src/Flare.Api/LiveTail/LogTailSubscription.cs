using System.Collections.Concurrent;
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

    public LogTailSubscription(int channelCapacity, string userKey = "")
    {
        UserKey = userKey;
        _channel = Channel.CreateBounded<LogTailServerMessage>(
            new BoundedChannelOptions(channelCapacity)
            {
                // A full channel means this connection's send loop can't keep up - drop the
                // write and count it (see OnDropped) rather than block the shared broadcaster
                // loop for every other subscriber, or silently evict via DropOldest. TryWrite
                // returns true for a dropped item in DropWrite mode, so the drop is observed
                // through the itemDropped callback instead of the return value.
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false, // the broadcaster loop and this connection's own receive loop (error replies) both write.
            },
            OnDropped);
    }

    private void OnDropped(LogTailServerMessage message)
    {
        // A dropped protocol error reply is not backpressure on the event feed.
        if (message.Type != LogTailServerMessageType.Event)
        {
            return;
        }

        Interlocked.Increment(ref _droppedSinceLastReport);
        if (DroppedSampleLimit > 0 && message.Event is { } logEvent && _droppedSample.Count < DroppedSampleLimit)
        {
            _droppedSample.Enqueue(logEvent);
        }
    }

    /// <summary>The identity this connection counts against for <see cref="LiveTailOptions.MaxConnectionsPerUser"/>.</summary>
    public string UserKey { get; }

    /// <summary>The service allow-list of the connecting user (ADR-0123), captured at subscribe time; null = unrestricted.</summary>
    public IReadOnlyList<string>? AllowedServices { get; } = Query.ServiceScope.Current;

    /// <summary>The current filter, applied by <see cref="LogTailBroadcaster"/> at fan-out time. Replaced wholesale on every <see cref="LogTailClientMessageType.Subscribe"/> message.</summary>
    public LogFilter Filter => _state.Filter;

    /// <summary>The regexes compiled for <see cref="Filter"/> at subscribe time, keyed by pattern text.</summary>
    public IReadOnlyDictionary<string, System.Text.RegularExpressions.Regex>? CompiledPatterns => _state.Patterns;

    /// <summary>Replaces the filter and its compiled patterns together, so the broadcaster never sees one without the other.</summary>
    public void SetFilter(LogFilter filter, IReadOnlyDictionary<string, System.Text.RegularExpressions.Regex>? patterns) =>
        _state = new FilterState(filter, patterns);

    private volatile FilterState _state = new(new LogFilter(), null);

    private sealed record FilterState(LogFilter Filter, IReadOnlyDictionary<string, System.Text.RegularExpressions.Regex>? Patterns);

    /// <summary>
    /// While <see langword="true"/>, <see cref="TryPublishEvent"/> is a no-op - events
    /// published while paused are dropped, not queued, and don't count toward
    /// <see cref="TakeDroppedCount"/> (that counter is specifically for backpressure, not
    /// a deliberate pause).
    /// </summary>
    public bool Paused { get; set; }

    public ChannelReader<LogTailServerMessage> Reader => _channel.Reader;

    private readonly ConcurrentQueue<LogEventDto> _droppedSample = new();

    /// <summary>
    /// How many events lost to a full channel are kept (not only counted) for <see cref="TakeDroppedEvents"/>; 0, the
    /// default, keeps none. Loki's tail protocol names each dropped entry (ADR-0238), so its endpoint turns this on.
    /// </summary>
    public int DroppedSampleLimit { get; set; }

    /// <summary>The events kept since the last call (at most <see cref="DroppedSampleLimit"/> per interval), oldest first.</summary>
    public IReadOnlyList<LogEventDto> TakeDroppedEvents()
    {
        var taken = new List<LogEventDto>();
        while (_droppedSample.TryDequeue(out var dropped))
        {
            taken.Add(dropped);
        }

        return taken;
    }

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

        _channel.Writer.TryWrite(new LogTailServerMessage { Type = LogTailServerMessageType.Event, Event = logEvent });
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
