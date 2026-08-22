using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.HostStats;

/// <summary>
/// The single shared reader behind every <c>GET /api/resources/host/watch</c> connection,
/// <c>GET /api/resources/host/snapshot</c> request, and <c>GET /api/resources/host/history</c>
/// request: samples <c>/proc</c> on an interval, fans the computed snapshot out to every
/// subscriber, and retains two independent rolling windows - a 1h/2s-cadence one for the
/// Resource trends chart, and a much coarser, multi-day one just for the disk growth-rate
/// figure (see the class remarks below) - same "one instance, two roles" pattern as
/// <c>DockerResources.DockerContainerPoller</c> (registered as both a singleton, so
/// <c>Endpoints.HostStatsEndpoints</c> can call <see cref="Subscribe"/>/
/// <see cref="Unsubscribe"/> and read <see cref="CurrentSnapshot"/>/<see cref="GetHistory"/>,
/// and a hosted service, for <see cref="ExecuteAsync"/> - wired in <c>Program.cs</c>).
/// </summary>
/// <remarks>
/// <para>
/// The Resource trends history (<see cref="_history"/>) and the disk growth-rate buffer
/// (<see cref="_diskGrowthSamples"/>) are each a plain <see cref="List{T}"/> guarded by
/// their own lock, not a lock-free structure - writes are one per tick (or, for disk
/// growth, one per <see cref="HostStatsOptions.DiskGrowthSampleInterval"/>) from this
/// single background loop, reads are occasional HTTP requests, so there's no real
/// contention to optimize for. Both are trimmed by elapsed time, not a fixed sample count,
/// so they stay correct if their respective intervals ever change. In-memory only, same
/// "live diagnostic, not durable history" spirit the rest of this feature already has - a
/// restart resets both (the growth-rate figure specifically needs
/// <see cref="HostStatsOptions.DiskGrowthMinimumSpan"/> of uptime to report anything again
/// after one).
/// </para>
/// <para>
/// Network (<c>/proc/net/dev</c>) and disk I/O (<c>/proc/diskstats</c>) both need a rate,
/// not a raw cumulative count - unlike CPU% (pure OS tick-accounting, no wall clock
/// involved), a byte/packet/sector delta needs the *actual* elapsed time between samples,
/// which drifts a little past the nominal <see cref="HostStatsOptions.PollDelay"/>
/// (<c>Task.Delay</c> plus this loop's own processing time), so both are diffed against
/// <see cref="TimeProvider.GetUtcNow"/> timestamps captured per tick rather than assuming
/// <c>PollDelay</c> exactly.
/// </para>
/// </remarks>
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
    private const string ProcNetDevPath = "/proc/net/dev";
    private const string ProcDiskStatsPath = "/proc/diskstats";

    private static readonly HostStatsSnapshot NotAvailableSnapshot = new()
    {
        Available = false,
        UnavailableReason =
            "Host stats need Linux's /proc filesystem, which isn't present here (e.g. " +
            "Flare.Api running as a bare process on a non-Linux Aspire dev loop). This " +
            "works out of the box under docker-compose.",
    };

    /// <summary>One disk growth-rate buffer sample - see the class remarks.</summary>
    internal readonly record struct DiskGrowthSample(DateTimeOffset Timestamp, long UsedBytes);

    /// <summary>Threaded through <see cref="ExecuteAsync"/>'s loop - every "previous tick" value <see cref="PollOnceAsync"/> needs to diff against, bundled so the loop signature doesn't grow a field at a time.</summary>
    private sealed record PreviousSamples(
        ProcFileReader.CpuSample? Cpu,
        IReadOnlyList<ProcFileReader.CpuSample>? PerCore,
        (ProcFileReader.NetSample Sample, DateTimeOffset SampledAt)? Net,
        (ProcFileReader.DiskIoSample Sample, DateTimeOffset SampledAt)? DiskIo)
    {
        internal static readonly PreviousSamples None = new(null, null, null, null);
    }

    private readonly ConcurrentDictionary<HostStatsSubscription, byte> _subscriptions = new();
    private readonly object _historyLock = new();
    private readonly List<HostStatsHistoryPoint> _history = [];
    private readonly object _diskGrowthLock = new();
    private readonly List<DiskGrowthSample> _diskGrowthSamples = [];
    private HostStatsSnapshot _currentSnapshot = NotAvailableSnapshot;

    /// <summary>The most recently published snapshot - what <c>GET /api/resources/host/snapshot</c> returns, with no live <c>/proc</c> read on the request path.</summary>
    public HostStatsSnapshot CurrentSnapshot
    {
        get => Volatile.Read(ref _currentSnapshot);
        private set => Volatile.Write(ref _currentSnapshot, value);
    }

    /// <summary>The retained Resource-trends rolling window (see the class remarks) - what <c>GET /api/resources/host/history</c> returns, oldest first.</summary>
    public IReadOnlyList<HostStatsHistoryPoint> GetHistory()
    {
        lock (_historyLock)
        {
            return _history.ToArray();
        }
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

        var previous = PreviousSamples.None;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                previous = await PollOnceAsync(previous, stoppingToken);
                await Task.Delay(options.Value.PollDelay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task<PreviousSamples> PollOnceAsync(PreviousSamples previous, CancellationToken cancellationToken)
    {
        try
        {
            // Read once, parse twice - ParseStat (aggregate) and ParseStatPerCore share the
            // same source text, no reason to hit the file twice for it.
            var statText = await File.ReadAllTextAsync(ProcStatPath, cancellationToken);
            var currentCpu = ProcFileReader.ParseStat(statText);
            var currentPerCore = ProcFileReader.ParseStatPerCore(statText);

            var memInfoText = await File.ReadAllTextAsync(ProcMemInfoPath, cancellationToken);
            var memInfo = ProcFileReader.ParseMemInfo(memInfoText);

            var uptimeText = await File.ReadAllTextAsync(ProcUptimePath, cancellationToken);
            var uptimeSeconds = ProcFileReader.ParseUptimeSeconds(uptimeText);

            // /proc/loadavg, /proc/net/dev, and /proc/diskstats are all best-effort - none
            // are as universally guaranteed as /proc/stat/meminfo/uptime, but there's no
            // reason to lose the rest of the snapshot over any one of them specifically.
            var loadAverage1m = 0.0;
            try
            {
                var loadAvgText = await File.ReadAllTextAsync(ProcLoadAvgPath, cancellationToken);
                loadAverage1m = ProcFileReader.ParseLoadAverage1m(loadAvgText);
            }
            catch (IOException)
            {
            }

            ProcFileReader.NetSample? currentNet = null;
            try
            {
                var netDevText = await File.ReadAllTextAsync(ProcNetDevPath, cancellationToken);
                currentNet = ProcFileReader.ParseNetDev(netDevText);
            }
            catch (IOException)
            {
            }

            ProcFileReader.DiskIoSample? currentDiskIo = null;
            try
            {
                var diskStatsText = await File.ReadAllTextAsync(ProcDiskStatsPath, cancellationToken);
                currentDiskIo = ProcFileReader.ParseDiskStats(diskStatsText);
            }
            catch (IOException)
            {
            }

            var (diskTotal, diskUsed) = diskReader.Read();
            var now = timeProvider.GetUtcNow();

            // No prior sample yet (first tick since startup) - CPU% starts at 0.0 and
            // corrects itself next tick, same spirit as DockerContainerPoller.Subscribe
            // not blocking on a full poll before publishing something.
            var cpuUsagePercent = previous.Cpu is { } previousCpu
                ? ProcFileReader.CalculateCpuUsagePercent(previousCpu, currentCpu)
                : 0.0;

            var perCoreUsagePercent = CalculatePerCoreUsagePercent(previous.PerCore, currentPerCore);

            var (networkRxRate, networkTxRate, networkPacketRate) = CalculateNetworkRates(previous.Net, currentNet, now);
            var (diskReadRate, diskWriteRate) = CalculateDiskIoRates(previous.DiskIo, currentDiskIo, now);

            var memoryUsedBytes = memInfo.TotalBytes - memInfo.AvailableBytes;
            var swapUsedBytes = Math.Max(0, memInfo.SwapTotalBytes - memInfo.SwapFreeBytes);
            var (diskGrowthBytesPerDay, diskGrowthWindowHours) = UpdateDiskGrowth(now, diskUsed);

            var snapshot = new HostStatsSnapshot
            {
                Available = true,
                CpuUsagePercent = cpuUsagePercent,
                CpuCoreCount = currentCpu.CoreCount,
                LoadAverage1m = loadAverage1m,
                PerCoreUsagePercent = perCoreUsagePercent,
                MemoryTotalBytes = memInfo.TotalBytes,
                MemoryUsedBytes = memoryUsedBytes,
                MemoryAvailableBytes = memInfo.AvailableBytes,
                SwapTotalBytes = memInfo.SwapTotalBytes,
                SwapUsedBytes = swapUsedBytes,
                DiskTotalBytes = diskTotal,
                DiskUsedBytes = diskUsed,
                DiskAvailableBytes = diskTotal - diskUsed,
                DiskGrowthBytesPerDay = diskGrowthBytesPerDay,
                DiskGrowthWindowHours = diskGrowthWindowHours,
                DiskReadBytesPerSecond = diskReadRate,
                DiskWriteBytesPerSecond = diskWriteRate,
                NetworkBytesPerSecond = networkRxRate + networkTxRate,
                NetworkRxBytesPerSecond = networkRxRate,
                NetworkTxBytesPerSecond = networkTxRate,
                NetworkPacketsPerSecond = networkPacketRate,
                UptimeSeconds = uptimeSeconds,
                UpdatedAt = now,
            };
            Publish(snapshot);

            AppendHistory(new HostStatsHistoryPoint
            {
                Timestamp = now,
                CpuUsagePercent = cpuUsagePercent,
                MemoryUsedPercent = memInfo.TotalBytes > 0 ? 100.0 * memoryUsedBytes / memInfo.TotalBytes : 0.0,
                DiskUsedPercent = diskTotal > 0 ? 100.0 * diskUsed / diskTotal : 0.0,
                NetworkBytesPerSecond = snapshot.NetworkBytesPerSecond,
            });

            return new PreviousSamples(
                currentCpu,
                currentPerCore,
                currentNet is { } net ? (net, now) : previous.Net,
                currentDiskIo is { } diskIo ? (diskIo, now) : previous.DiskIo);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A transient read failure isn't the same as "not available" (Available stays
            // whatever it last was - the panel keeps showing the last good numbers rather
            // than flashing to an error state over one bad tick).
            logger.LogWarning(ex, "Failed to sample host stats from /proc.");
            return previous;
        }
    }

    /// <summary>Per-core delta, same shape as <see cref="ProcFileReader.CalculateCpuUsagePercent"/> but one call per core. Empty (not one entry per core at 0.0) when there's no previous sample to diff against yet, or its core count doesn't match the current one (a mid-tick CPU hot-add/remove - vanishingly unlikely in a container, but this stays defensive rather than throwing or misaligning indices) - self-corrects on the next tick either way.</summary>
    internal static IReadOnlyList<double> CalculatePerCoreUsagePercent(
        IReadOnlyList<ProcFileReader.CpuSample>? previous,
        IReadOnlyList<ProcFileReader.CpuSample> current)
    {
        if (previous is null || previous.Count != current.Count || current.Count == 0)
        {
            return [];
        }

        var result = new double[current.Count];
        for (var i = 0; i < current.Count; i++)
        {
            result[i] = ProcFileReader.CalculateCpuUsagePercent(previous[i], current[i]);
        }

        return result;
    }

    internal static (double RxBytesPerSecond, double TxBytesPerSecond, double PacketsPerSecond) CalculateNetworkRates(
        (ProcFileReader.NetSample Sample, DateTimeOffset SampledAt)? previous,
        ProcFileReader.NetSample? current,
        DateTimeOffset now)
    {
        if (current is not { } currentSample || previous is not { } previousSample)
        {
            return (0.0, 0.0, 0.0);
        }

        var elapsedSeconds = (now - previousSample.SampledAt).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            return (0.0, 0.0, 0.0);
        }

        var rxRate = Math.Max(0, (currentSample.RxBytes - previousSample.Sample.RxBytes) / elapsedSeconds);
        var txRate = Math.Max(0, (currentSample.TxBytes - previousSample.Sample.TxBytes) / elapsedSeconds);
        var packetDelta = (currentSample.RxPackets - previousSample.Sample.RxPackets) +
            (currentSample.TxPackets - previousSample.Sample.TxPackets);
        return (rxRate, txRate, Math.Max(0, packetDelta / elapsedSeconds));
    }

    internal static (double ReadBytesPerSecond, double WriteBytesPerSecond) CalculateDiskIoRates(
        (ProcFileReader.DiskIoSample Sample, DateTimeOffset SampledAt)? previous,
        ProcFileReader.DiskIoSample? current,
        DateTimeOffset now)
    {
        if (current is not { } currentSample || previous is not { } previousSample)
        {
            return (0.0, 0.0);
        }

        var elapsedSeconds = (now - previousSample.SampledAt).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            return (0.0, 0.0);
        }

        var readRate = Math.Max(0, (currentSample.ReadBytes - previousSample.Sample.ReadBytes) / elapsedSeconds);
        var writeRate = Math.Max(0, (currentSample.WriteBytes - previousSample.Sample.WriteBytes) / elapsedSeconds);
        return (readRate, writeRate);
    }

    private void Publish(HostStatsSnapshot snapshot)
    {
        CurrentSnapshot = snapshot;
        foreach (var subscription in _subscriptions.Keys)
        {
            subscription.Publish(snapshot);
        }
    }

    private void AppendHistory(HostStatsHistoryPoint point)
    {
        lock (_historyLock)
        {
            _history.Add(point);
            TrimOlderThan(_history, point.Timestamp - options.Value.HistoryWindow);
        }
    }

    /// <summary>
    /// Removes every point at or before <paramref name="cutoff"/> from the front of
    /// <paramref name="history"/>. Internal (not private) so <c>Flare.Api.Tests</c> can
    /// exercise the trim boundary directly, same testability convention
    /// <c>DockerResources.DockerContainerPoller</c>'s <c>BuildSnapshot</c> uses - assumes
    /// <paramref name="history"/> is already in ascending timestamp order (true here, since
    /// <see cref="AppendHistory"/> only ever adds at the end), so it can stop at the first
    /// point that's still within the window rather than scanning the whole list.
    /// </summary>
    internal static void TrimOlderThan(List<HostStatsHistoryPoint> history, DateTimeOffset cutoff)
    {
        var removeCount = 0;
        while (removeCount < history.Count && history[removeCount].Timestamp <= cutoff)
        {
            removeCount++;
        }

        if (removeCount > 0)
        {
            history.RemoveRange(0, removeCount);
        }
    }

    /// <summary>Conditionally appends this tick's disk-used-bytes to the growth buffer (only when at least <see cref="HostStatsOptions.DiskGrowthSampleInterval"/> has passed since the last stored sample), trims it, and returns the up-to-date growth figures - see the class remarks and <see cref="CalculateDiskGrowth"/>.</summary>
    private (double? BytesPerDay, double WindowHours) UpdateDiskGrowth(DateTimeOffset now, long usedBytes)
    {
        lock (_diskGrowthLock)
        {
            if (_diskGrowthSamples.Count == 0 ||
                now - _diskGrowthSamples[^1].Timestamp >= options.Value.DiskGrowthSampleInterval)
            {
                _diskGrowthSamples.Add(new DiskGrowthSample(now, usedBytes));
            }

            TrimDiskGrowthSamples(_diskGrowthSamples, now - options.Value.DiskGrowthWindow);
            return CalculateDiskGrowth(_diskGrowthSamples, now, usedBytes, options.Value.DiskGrowthMinimumSpan);
        }
    }

    /// <summary>Same shape as <see cref="TrimOlderThan"/>, for the disk growth buffer - kept as a separate method (rather than a generic helper) since the two lists hold different element types and are trimmed against different cutoffs/windows.</summary>
    internal static void TrimDiskGrowthSamples(List<DiskGrowthSample> samples, DateTimeOffset cutoff)
    {
        var removeCount = 0;
        while (removeCount < samples.Count && samples[removeCount].Timestamp <= cutoff)
        {
            removeCount++;
        }

        if (removeCount > 0)
        {
            samples.RemoveRange(0, removeCount);
        }
    }

    /// <summary>
    /// Pure growth-rate math, split out from <see cref="UpdateDiskGrowth"/> purely for
    /// testability (same "internal for testability" convention every other pure method in
    /// this feature uses). <see langword="null"/> until the oldest retained sample is at
    /// least <paramref name="minimumSpan"/> old - a rate extrapolated from a handful of
    /// samples would be noise dressed up as a number (see
    /// <see cref="HostStatsOptions.DiskGrowthMinimumSpan"/>'s remarks). <c>WindowHours</c>
    /// is still returned even when the rate itself is <see langword="null"/>, so a caller
    /// can tell "12 more minutes until this reports anything" from "reports nothing, ever,
    /// because the buffer is empty."
    /// </summary>
    internal static (double? BytesPerDay, double WindowHours) CalculateDiskGrowth(
        IReadOnlyList<DiskGrowthSample> samples,
        DateTimeOffset now,
        long currentUsedBytes,
        TimeSpan minimumSpan)
    {
        if (samples.Count == 0)
        {
            return (null, 0.0);
        }

        var oldest = samples[0];
        var span = now - oldest.Timestamp;
        if (span < minimumSpan || span <= TimeSpan.Zero)
        {
            return (null, Math.Max(0.0, span.TotalHours));
        }

        var bytesPerDay = (currentUsedBytes - oldest.UsedBytes) / span.TotalDays;
        return (bytesPerDay, span.TotalHours);
    }
}
