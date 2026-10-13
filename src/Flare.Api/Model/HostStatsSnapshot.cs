using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// The full payload behind both <c>GET /api/resources/host/snapshot</c> and every message
/// pushed over <c>GET /api/resources/host/watch</c> - the Resources page's "Host overview"
/// panel. See <c>HostStats.HostStatsPoller</c> for how it's sampled and broadcast.
/// </summary>
[MemoryPackable]
public sealed partial record HostStatsSnapshot
{
    /// <summary>
    /// <see langword="false"/> means this host's stats genuinely can't be read - not Linux,
    /// or <c>/proc/stat</c> missing (see <c>HostStats.HostStatsPoller</c>'s startup check) -
    /// which happens on a non-Linux Aspire dev loop (e.g. a Mac running <c>Flare.Api</c>
    /// directly). Unlike <see cref="ResourceGraphSnapshot.Available"/> there's no separate
    /// "configured but unreachable" state to distinguish - this either can read
    /// <c>/proc</c> or it can't.
    /// </summary>
    public required bool Available { get; init; }

    /// <summary>Human-readable explanation, set whenever <see cref="Available"/> is <see langword="false"/> - <see langword="null"/> once real data is showing.</summary>
    public string? UnavailableReason { get; init; }

    /// <summary>0-100. 0.0 on the very first sample (no prior <c>/proc/stat</c> reading to diff against yet) - corrects itself on the next poll tick.</summary>
    public double CpuUsagePercent { get; init; }

    /// <summary>True host core count, from counting <c>/proc/stat</c>'s <c>cpuN</c> lines - see <c>HostStats.ProcFileReader.ParseStat</c>'s remarks for why this isn't <see cref="Environment.ProcessorCount"/>.</summary>
    public int CpuCoreCount { get; init; }

    /// <summary>1-minute load average from <c>/proc/loadavg</c>.</summary>
    public double LoadAverage1m { get; init; }

    /// <summary>Per-core usage percent, same 0-100/delta-based shape as <see cref="CpuUsagePercent"/> - index <c>i</c> corresponds to <c>cpuN</c> where <c>N == i</c>. Empty on the very first sample (no prior per-core tick to diff against), same as <see cref="CpuUsagePercent"/> starting at 0.0. Feeds the Host overview panel's per-core breakdown, not the Resource trends chart - see that panel's own remarks on why per-core stays instantaneous-only.</summary>
    public IReadOnlyList<double> PerCoreUsagePercent { get; init; } = [];

    public long MemoryTotalBytes { get; init; }

    public long MemoryUsedBytes { get; init; }

    /// <summary><c>MemAvailable</c> from <c>/proc/meminfo</c> - not simply <see cref="MemoryTotalBytes"/> minus <see cref="MemoryUsedBytes"/> restated (it *is* that value, by construction - <c>MemoryUsedBytes</c> is derived from it - but shown as its own field so the frontend never has to re-derive it, and so the two stay obviously consistent).</summary>
    public long MemoryAvailableBytes { get; init; }

    /// <summary>0 on a host with swap disabled entirely - common under Docker Desktop and various cloud VM images. The frontend hides the swap line whenever this is 0, rather than showing "0 B swap" everywhere.</summary>
    public long SwapTotalBytes { get; init; }

    public long SwapUsedBytes { get; init; }

    public long DiskTotalBytes { get; init; }

    public long DiskUsedBytes { get; init; }

    /// <summary><see cref="DiskTotalBytes"/> minus <see cref="DiskUsedBytes"/> - see <see cref="MemoryAvailableBytes"/>'s remarks on why this is its own field rather than left to the frontend to subtract.</summary>
    public long DiskAvailableBytes { get; init; }

    /// <summary>
    /// Bytes/sec, from a much longer-lived buffer than the rest of this snapshot (days, not
    /// the Resource trends chart's 1h) - see <c>HostStats.HostStatsPoller</c>'s remarks.
    /// <see langword="null"/> until <c>HostStatsOptions.DiskGrowthMinimumSpan</c> worth of
    /// samples has accumulated (a rate extrapolated from a few minutes of data is noise
    /// dressed up as a number) - the frontend omits the growth line entirely rather than
    /// showing a placeholder while this is <see langword="null"/>.
    /// </summary>
    public double? DiskGrowthBytesPerDay { get; init; }

    /// <summary>How much actual span <see cref="DiskGrowthBytesPerDay"/> was computed over - always shown alongside it (e.g. "based on the trailing 6h") so an early, still-warming-up buffer never silently implies a stable day-over-day figure. Settles at (and stays near) 24 once the buffer holds a full day.</summary>
    public double DiskGrowthWindowHours { get; init; }

    /// <summary>Best-effort - see <c>HostStats.ProcFileReader.ParseDiskStats</c>'s remarks on the whole-disk-vs-partition heuristic this sums across. 0.0 on the very first sample, same reasoning as <see cref="CpuUsagePercent"/>.</summary>
    public double DiskReadBytesPerSecond { get; init; }

    public double DiskWriteBytesPerSecond { get; init; }

    /// <summary>Bytes/sec, Rx + Tx combined across every non-loopback interface - what the Resource trends chart's "Network" line still plots. See <see cref="NetworkRxBytesPerSecond"/>/<see cref="NetworkTxBytesPerSecond"/> for the split the Host overview panel's compact live line shows instead.</summary>
    public double NetworkBytesPerSecond { get; init; }

    public double NetworkRxBytesPerSecond { get; init; }

    public double NetworkTxBytesPerSecond { get; init; }

    /// <summary>Packets/sec, Rx + Tx combined - the least specific of these fields (no split requested), one number rather than two.</summary>
    public double NetworkPacketsPerSecond { get; init; }

    /// <summary>Seconds since the host booted, from <c>/proc/uptime</c> - not this process's own uptime.</summary>
    public double UptimeSeconds { get; init; }

    /// <summary><see langword="null"/> until the first successful sample completes.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
