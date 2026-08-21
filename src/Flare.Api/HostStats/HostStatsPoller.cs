using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.HostStats;

/// <summary>
/// The single shared reader behind every <c>GET /api/resources/host/watch</c> connection
/// and <c>GET /api/resources/host/snapshot</c> request: samples <c>/proc</c> on an
/// interval and fans the computed snapshot out to every subscriber - same "one instance,
/// two roles" pattern as <c>DockerResources.DockerContainerPoller</c> (registered as both a
/// singleton, so <c>Endpoints.HostStatsEndpoints</c> can call
/// <see cref="Subscribe"/>/<see cref="Unsubscribe"/> and read <see cref="CurrentSnapshot"/>,
/// and a hosted service, for <see cref="ExecuteAsync"/> - wired in <c>Program.cs</c>).
/// </summary>
public sealed class HostStatsPoller(
    IOptions<HostStatsOptions> options,
    HostDiskReader diskReader,
    TimeProvider timeProvider,
    ILogger<HostStatsPoller> logger) : BackgroundService
{
    private const string ProcStatPath = "/proc/stat";
    private const string ProcMemInfoPath = "/proc/meminfo";
    private const string ProcUptimePath = "/proc/uptime";
    private const string ProcLoadAvgPath = "/proc/loadavg";

    private static readonly HostStatsSnapshot NotAvailableSnapshot = new()
    {
        Available = false,
        UnavailableReason =
            "Host stats need Linux's /proc filesystem, which isn't present here (e.g. " +
            "Flare.Api running as a bare process on a non-Linux Aspire dev loop). This " +
            "works out of the box under docker-compose.",
    };

    private readonly ConcurrentDictionary<HostStatsSubscription, byte> _subscriptions = new();
    private HostStatsSnapshot _currentSnapshot = NotAvailableSnapshot;

    /// <summary>The most recently published snapshot - what <c>GET /api/resources/host/snapshot</c> returns, with no live <c>/proc</c> read on the request path.</summary>
    public HostStatsSnapshot CurrentSnapshot
    {
        get => Volatile.Read(ref _currentSnapshot);
        private set => Volatile.Write(ref _currentSnapshot, value);
    }

    public HostStatsSubscription Subscribe()
    {
        var subscription = new HostStatsSubscription();
        _subscriptions[subscription] = 0;
        // Don't make a fresh connection wait up to a full PollDelay for its first snapshot.
        subscription.Publish(CurrentSnapshot);
        return subscription;
    }

    public void Unsubscribe(HostStatsSubscription subscription)
    {
        _subscriptions.TryRemove(subscription, out _);
        subscription.Complete();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || !File.Exists(ProcStatPath))
        {
            // Not available on this platform - CurrentSnapshot stays NotAvailableSnapshot
            // forever, same "nothing else to do" shape as DockerContainerPoller's
            // absent-ProxyUrl early return.
            return;
        }

        ProcFileReader.CpuSample? previousCpuSample = null;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                previousCpuSample = await PollOnceAsync(previousCpuSample, stoppingToken);
                await Task.Delay(options.Value.PollDelay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task<ProcFileReader.CpuSample?> PollOnceAsync(ProcFileReader.CpuSample? previousCpuSample, CancellationToken cancellationToken)
    {
        try
        {
            var statText = await File.ReadAllTextAsync(ProcStatPath, cancellationToken);
            var currentCpuSample = ProcFileReader.ParseStat(statText);

            var memInfoText = await File.ReadAllTextAsync(ProcMemInfoPath, cancellationToken);
            var memInfo = ProcFileReader.ParseMemInfo(memInfoText);

            var uptimeText = await File.ReadAllTextAsync(ProcUptimePath, cancellationToken);
            var uptimeSeconds = ProcFileReader.ParseUptimeSeconds(uptimeText);

            // /proc/loadavg is best-effort - present on every real Linux system, but
            // there's no reason to lose the rest of the snapshot over it specifically.
            var loadAverage1m = 0.0;
            try
            {
                var loadAvgText = await File.ReadAllTextAsync(ProcLoadAvgPath, cancellationToken);
                loadAverage1m = ProcFileReader.ParseLoadAverage1m(loadAvgText);
            }
            catch (IOException)
            {
            }

            var (diskTotal, diskUsed) = diskReader.Read();

            // No prior sample yet (first tick since startup) - CPU% starts at 0.0 and
            // corrects itself next tick, same spirit as DockerContainerPoller.Subscribe
            // not blocking on a full poll before publishing something.
            var cpuUsagePercent = previousCpuSample is { } previous
                ? ProcFileReader.CalculateCpuUsagePercent(previous, currentCpuSample)
                : 0.0;

            Publish(new HostStatsSnapshot
            {
                Available = true,
                CpuUsagePercent = cpuUsagePercent,
                CpuCoreCount = currentCpuSample.CoreCount,
                LoadAverage1m = loadAverage1m,
                MemoryTotalBytes = memInfo.TotalBytes,
                MemoryUsedBytes = memInfo.TotalBytes - memInfo.AvailableBytes,
                DiskTotalBytes = diskTotal,
                DiskUsedBytes = diskUsed,
                UptimeSeconds = uptimeSeconds,
                UpdatedAt = timeProvider.GetUtcNow(),
            });

            return currentCpuSample;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A transient read failure isn't the same as "not available" (Available stays
            // whatever it last was - the panel keeps showing the last good numbers rather
            // than flashing to an error state over one bad tick).
            logger.LogWarning(ex, "Failed to sample host stats from /proc.");
            return previousCpuSample;
        }
    }

    private void Publish(HostStatsSnapshot snapshot)
    {
        CurrentSnapshot = snapshot;
        foreach (var subscription in _subscriptions.Keys)
        {
            subscription.Publish(snapshot);
        }
    }
}
