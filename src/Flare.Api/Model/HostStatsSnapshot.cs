namespace Flare.Api.Model;

/// <summary>
/// The full payload behind both <c>GET /api/resources/host/snapshot</c> and every message
/// pushed over <c>GET /api/resources/host/watch</c> - the Resources page's "Host overview"
/// panel. See <c>HostStats.HostStatsPoller</c> for how it's sampled and broadcast.
/// </summary>
public sealed record HostStatsSnapshot
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

    public long MemoryTotalBytes { get; init; }

    public long MemoryUsedBytes { get; init; }

    public long DiskTotalBytes { get; init; }

    public long DiskUsedBytes { get; init; }

    /// <summary>Seconds since the host booted, from <c>/proc/uptime</c> - not this process's own uptime.</summary>
    public double UptimeSeconds { get; init; }

    /// <summary><see langword="null"/> until the first successful sample completes.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
