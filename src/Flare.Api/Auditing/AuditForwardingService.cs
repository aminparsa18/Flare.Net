using Flare.Identity.Audit;
using Microsoft.Extensions.Options;

namespace Flare.Api.Auditing;

/// <summary>
/// Delivers audit events, oldest first, to each configured <see cref="IAuditSink"/> (ADR-0203).
/// Each sink has its own cursor in the Identity database, advanced only after a successful send,
/// so delivery is at-least-once and in id order; a failing sink retries the same batch and never
/// blocks the others.
/// </summary>
public sealed class AuditForwardingService(
    IAuditEventStore store,
    IReadOnlyList<IAuditSink> sinks,
    IOptions<AuditOptions> options,
    ILogger<AuditForwardingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sinkList = sinks;
        if (sinkList.Count == 0)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.Forwarding.IntervalSeconds));
        using var timer = new PeriodicTimer(interval);
        do
        {
            foreach (var sink in sinkList)
            {
                await ForwardAsync(sink, stoppingToken);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Drains everything pending for one sink; returns the number of events delivered.</summary>
    internal async Task<int> ForwardAsync(IAuditSink sink, CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(options.Value.Forwarding.BatchSize, 1, 1000);
        var delivered = 0;
        try
        {
            var cursor = await store.GetForwardingCursorAsync(sink.Name, cancellationToken);
            while (true)
            {
                var batch = await store.QueryAfterAsync(cursor, batchSize, cancellationToken);
                if (batch.Count == 0)
                {
                    return delivered;
                }

                await sink.SendAsync(batch, cancellationToken);
                cursor = batch[^1].Id;
                await store.SetForwardingCursorAsync(sink.Name, cursor, cancellationToken);
                delivered += batch.Count;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Audit forwarding to {Sink} failed after {Delivered} events; will retry", sink.Name, delivered);
            return delivered;
        }
    }
}
