using Flare.Api.Model;
using Flare.Api.Query;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.AlertWorker.CheckIns;

/// <summary>
/// Judges every enabled <see cref="CheckInMonitor"/> against the pings it has received and records
/// <see cref="CheckInMetrics.Up"/>. See <c>docs-internal/adr/0260-cron-job-checkins.md</c>.
/// </summary>
/// <remarks>
/// Every replica runs this loop, so each tick is claimed with <c>SET NX</c> on one Redis key that expires after the poll
/// interval: whichever replica sets it judges all monitors for that tick, the rest skip it. A replica that dies after
/// claiming loses one tick of points, and the next tick writes them again.
/// </remarks>
public sealed class CheckInWorker(
    ICheckInMonitorQueryService monitors,
    CheckInResultWriter writer,
    IConnectionMultiplexer redis,
    IOptions<CheckInOptions> options,
    TimeProvider timeProvider,
    ILogger<CheckInWorker> logger) : BackgroundService
{
    private const string TickKey = "flare:checkin:tick";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("Check-in monitoring is disabled (CheckIn:Enabled=false).");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await redis.GetDatabase().StringSetAsync(TickKey, "1", opts.PollInterval * 0.9, When.NotExists))
                {
                    await TickAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Check-in tick failed; retrying next tick.");
            }

            await Task.Delay(opts.PollInterval, timeProvider, stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var enabled = (await monitors.ListAsync(cancellationToken)).Where(m => m.Enabled).ToList();
        if (enabled.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var pings = await monitors.PingsAsync(enabled.Select(m => m.Slug).ToList(), cancellationToken);
        var results = new List<(string Slug, bool Up)>(enabled.Count);
        foreach (var monitor in enabled)
        {
            var status = CheckInEvaluator.Evaluate(monitor, pings.GetValueOrDefault(monitor.Slug, CheckInPings.None), now);
            if (!status.Up)
            {
                logger.LogDebug("Check-in monitor {Slug} is {State}.", monitor.Slug, status.State);
            }

            results.Add((monitor.Slug, status.Up));
        }

        await writer.WriteAsync(results, now, cancellationToken);
    }
}
