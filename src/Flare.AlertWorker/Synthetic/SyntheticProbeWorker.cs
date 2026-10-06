using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Api.Synthetic;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.AlertWorker.Synthetic;

/// <summary>
/// Runs every enabled <see cref="SyntheticMonitor"/> at its own interval and records the result as gauge
/// metrics. See <c>docs-internal/adr/0128-synthetic-monitoring.md</c>.
/// </summary>
/// <remarks>
/// Every replica runs this loop, so a probe is claimed with <c>SET NX</c> on a per-monitor Redis key whose
/// expiry is the monitor's interval: whichever replica sets it runs that probe, the rest skip it until the key
/// expires. Unlike <see cref="Alerting.AlertEvaluationWorker"/> there is no per-tick lock - monitors are
/// independent, so replicas can split them. A replica that dies after claiming loses at most one probe.
/// </remarks>
public sealed class SyntheticProbeWorker(
    ISyntheticMonitorQueryService monitors,
    SyntheticProber prober,
    SyntheticResultWriter writer,
    IConnectionMultiplexer redis,
    IOptions<SyntheticOptions> options,
    TimeProvider timeProvider,
    ILogger<SyntheticProbeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("Synthetic probing is disabled (Synthetic:Enabled=false).");
            return;
        }

        using var gate = new SemaphoreSlim(Math.Max(1, opts.MaxConcurrency));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(gate, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Synthetic probe tick failed; retrying next tick.");
            }

            await Task.Delay(opts.PollInterval, timeProvider, stoppingToken);
        }
    }

    private async Task TickAsync(SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();
        var running = new List<Task>();
        foreach (var monitor in await monitors.ListAsync(cancellationToken))
        {
            if (!monitor.Enabled
                || !await db.StringSetAsync(ClaimKey(monitor.Id), "1", TimeSpan.FromSeconds(monitor.IntervalSeconds), When.NotExists))
            {
                continue;
            }

            await gate.WaitAsync(cancellationToken);
            running.Add(RunAsync(monitor, gate, cancellationToken));
        }

        await Task.WhenAll(running);
    }

    private async Task RunAsync(SyntheticMonitor monitor, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        try
        {
            var result = await prober.ProbeAsync(monitor, cancellationToken);
            await writer.WriteAsync(monitor, result, timeProvider.GetUtcNow(), cancellationToken);
            if (!result.Up)
            {
                logger.LogDebug("Monitor {Monitor} ({Target}) is down: {Error}", monitor.Name, monitor.Target, result.Error);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // One monitor's failure (a ClickHouse hiccup writing its result) must not stop the others.
            logger.LogWarning(ex, "Synthetic probe for monitor {Monitor} failed to record.", monitor.Name);
        }
        finally
        {
            gate.Release();
        }
    }

    private static RedisKey ClaimKey(Guid monitorId) => $"flare:synthetic:claim:{monitorId:N}";
}
