using System.Collections.Concurrent;
using System.Text.Json;
using Flare.Api.Pipeline;
using Flare.Api.Query;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.Api.LiveTail;

/// <summary>
/// The single shared reader behind every <c>GET /api/logs/tail</c> connection: polls
/// <see cref="LogEventPipelineOptions.StreamKey"/> via a plain <c>XREAD</c> (no consumer group -
/// deliberately doesn't join <c>Flare.Ingest</c>'s <c>flare-ingest</c> group or touch its
/// ack/PEL accounting; a live tail has no durability requirement, unlike the ingest
/// pipeline) and fans each new entry out to every subscribed connection's
/// <see cref="LogTailSubscription"/>, after applying that subscription's current filter.
/// </summary>
/// <remarks>
/// Registered as both a singleton (so <c>Endpoints.LogTailEndpoints</c> can call
/// <see cref="Subscribe"/>/<see cref="Unsubscribe"/>) and a hosted service (for
/// <see cref="ExecuteAsync"/>) - the standard "one instance, two roles" ASP.NET Core
/// pattern, wired in <c>Program.cs</c>.
///
/// Seeds its starting stream ID once at startup (the current last entry ID, via a
/// descending <c>XRANGE COUNT 1</c>) so it only ever sees entries added after
/// <c>Flare.Api</c> started - a live tail has no "catch up from the beginning" requirement.
/// If <c>Flare.Api</c> restarts, connected clients simply reconnect and resume from
/// whatever's new at that point; nothing is replayed.
/// </remarks>
public sealed class LogTailBroadcaster(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<LiveTailOptions> options,
    IOptions<LogEventPipelineOptions> logPipelineOptions,
    ILogger<LogTailBroadcaster> logger) : BackgroundService
{
    private static readonly RedisValue DataField = "data";
    private static readonly RedisValue StreamStart = "0-0";

    private readonly ConcurrentDictionary<LogTailSubscription, byte> _subscriptions = new();

    public LogTailSubscription Subscribe()
    {
        var subscription = new LogTailSubscription(options.Value.SubscriberChannelCapacity);
        _subscriptions[subscription] = 0;
        return subscription;
    }

    public void Unsubscribe(LogTailSubscription subscription)
    {
        _subscriptions.TryRemove(subscription, out _);
        subscription.Complete();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        var streamKey = logPipelineOptions.Value.StreamKey;
        var db = connectionMultiplexer.GetDatabase();

        try
        {
            var lastId = await SeedStartPositionAsync(db, streamKey);

            while (!stoppingToken.IsCancellationRequested)
            {
                var entries = await db.StreamReadAsync(streamKey, lastId, opts.ReadBatchSize);

                if (entries.Length == 0)
                {
                    await Task.Delay(opts.PollDelay, stoppingToken);
                    continue;
                }

                foreach (var entry in entries)
                {
                    lastId = entry.Id;
                    Publish(entry);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>
    /// Finds the current last entry ID (or <see cref="StreamStart"/> if the stream doesn't
    /// exist/is empty yet) so the first <c>XREAD</c> only returns entries added after this
    /// point - see the class remarks.
    /// </summary>
    private static async Task<RedisValue> SeedStartPositionAsync(IDatabase db, string streamKey)
    {
        var last = await db.StreamRangeAsync(streamKey, messageOrder: Order.Descending, count: 1);
        return last.Length > 0 ? last[0].Id : StreamStart;
    }

    private void Publish(StreamEntry entry)
    {
        var raw = entry[DataField];
        if (raw.IsNullOrEmpty)
        {
            return;
        }

        BufferedLogEvent? bufferedLogEvent;
        try
        {
            bufferedLogEvent = JsonSerializer.Deserialize((string)raw!, BufferedLogEventJsonContext.Default.BufferedLogEvent);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize stream entry {Id} for live tail; skipping.", entry.Id);
            return;
        }

        if (bufferedLogEvent is null)
        {
            return;
        }

        var dto = BufferedLogEventMapper.ToDto(bufferedLogEvent);

        foreach (var subscription in _subscriptions.Keys)
        {
            if (LogFilterMatcher.Matches(dto, subscription.Filter))
            {
                subscription.TryPublishEvent(dto);
            }
        }
    }
}
