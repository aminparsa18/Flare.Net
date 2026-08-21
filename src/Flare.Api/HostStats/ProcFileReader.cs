using System.Globalization;

namespace Flare.Api.HostStats;

/// <summary>
/// Pure parsing for the Linux <c>/proc</c> files <see cref="HostStatsPoller"/> samples.
/// Kept separate from the actual <c>File.ReadAllTextAsync</c> calls (see that type) purely
/// so <c>Flare.Api.Tests</c> can exercise every format against fixture strings without a
/// real <c>/proc</c> filesystem - same internal-for-testability convention
/// <c>DockerResources.DockerContainerPoller</c>'s <c>BuildSnapshot</c>/<c>ParseState</c>
/// use via <c>Flare.Api.csproj</c>'s <c>InternalsVisibleTo</c>.
/// </summary>
/// <remarks>
/// None of this is namespaced by Docker's default container isolation - a container reads
/// its <em>host's</em> <c>/proc/stat</c>/<c>/proc/meminfo</c>/<c>/proc/uptime</c>/
/// <c>/proc/loadavg</c>, not a cgroup-scoped view - which is exactly what makes this
/// feature work from inside Flare.Api's own container without any extra plumbing (compare
/// <see cref="HostDiskReader"/>, where disk genuinely does need a bind mount).
/// </remarks>
internal static class ProcFileReader
{
    /// <summary>One <c>/proc/stat</c> sample: the aggregate "cpu" line's idle/total tick counts, plus how many <c>cpuN</c> lines followed it (the true host core count, unaffected by any future cgroup CPU quota on this container - see <see cref="HostStatsPoller"/>'s remarks).</summary>
    internal readonly record struct CpuSample(long IdleTicks, long TotalTicks, int CoreCount);

    internal readonly record struct MemInfo(long TotalBytes, long AvailableBytes);

    /// <summary>
    /// Parses the aggregate <c>cpu ...</c> line (fields, in order: user, nice, system,
    /// idle, iowait, irq, softirq, steal, guest, guest_nice - trailing fields are kernel-
    /// version-dependent, so this only assumes the first four exist) plus a count of the
    /// per-core <c>cpu0</c>/<c>cpu1</c>/... lines that follow it.
    /// </summary>
    internal static CpuSample ParseStat(string procStatText)
    {
        long idle = 0;
        long total = 0;
        var coreCount = 0;

        foreach (var rawLine in procStatText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (!line.StartsWith("cpu", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Length > 3 && char.IsDigit(line[3]))
            {
                coreCount++;
                continue;
            }

            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5 || fields[0] != "cpu")
            {
                continue;
            }

            // fields[0] is the "cpu" label itself; fields[1..] are the tick counters.
            var ticks = new long[fields.Length - 1];
            for (var i = 1; i < fields.Length; i++)
            {
                ticks[i - 1] = long.Parse(fields[i], CultureInfo.InvariantCulture);
            }

            // idle + iowait, same convention top/htop use - iowait is still "not busy"
            // time even though the kernel accounts it separately from idle.
            idle = ticks[3] + (ticks.Length > 4 ? ticks[4] : 0);
            total = 0;
            foreach (var tick in ticks)
            {
                total += tick;
            }
        }

        return new CpuSample(idle, total, coreCount);
    }

    /// <summary>
    /// CPU busy percentage between two <see cref="ParseStat"/> samples taken
    /// <see cref="HostStatsOptions.PollDelay"/> apart - the standard delta technique
    /// (<c>busy_delta / total_delta</c>) <c>/proc/stat</c>'s cumulative-since-boot tick
    /// counters require, same as <c>top</c>/<c>htop</c>/<c>docker stats</c>. Zero total
    /// delta (two samples taken back-to-back, or a kernel tick-accounting hiccup) reads as
    /// 0% rather than dividing by zero.
    /// </summary>
    internal static double CalculateCpuUsagePercent(CpuSample previous, CpuSample current)
    {
        var totalDelta = current.TotalTicks - previous.TotalTicks;
        if (totalDelta <= 0)
        {
            return 0.0;
        }

        var idleDelta = current.IdleTicks - previous.IdleTicks;
        var busyDelta = totalDelta - idleDelta;
        return Math.Clamp(100.0 * busyDelta / totalDelta, 0.0, 100.0);
    }

    /// <summary>Parses <c>MemTotal</c>/<c>MemAvailable</c> (kB, converted to bytes). <c>MemAvailable</c> - not the smaller/cruder <c>MemFree</c> - is what <c>free -h</c>'s own "available" column reports too: free memory plus reclaimable cache/buffers, i.e. what's actually usable.</summary>
    internal static MemInfo ParseMemInfo(string procMemInfoText)
    {
        long totalKb = 0;
        long availableKb = 0;

        foreach (var rawLine in procMemInfoText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                totalKb = ParseKbField(line);
            }
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                availableKb = ParseKbField(line);
            }
        }

        return new MemInfo(totalKb * 1024, availableKb * 1024);
    }

    private static long ParseKbField(string line)
    {
        // e.g. "MemTotal:       16337408 kB" - split on whitespace, field [0] is the
        // "Label:" token, [1] is the value, [2] (if present) is always "kB" for the fields
        // this reads.
        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 1 ? long.Parse(fields[1], CultureInfo.InvariantCulture) : 0;
    }

    /// <summary>First field of <c>/proc/uptime</c> - seconds since boot, as a fractional value the kernel reports to hundredths-of-a-second precision. The second field (total idle time summed across cores) isn't used here.</summary>
    internal static double ParseUptimeSeconds(string procUptimeText)
    {
        var fields = procUptimeText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 0 ? double.Parse(fields[0], CultureInfo.InvariantCulture) : 0.0;
    }

    /// <summary>First field of <c>/proc/loadavg</c> - the 1-minute load average. Same file also carries 5m/15m averages and a runnable/total process count, neither used here.</summary>
    internal static double ParseLoadAverage1m(string procLoadAvgText)
    {
        var fields = procLoadAvgText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 0 ? double.Parse(fields[0], CultureInfo.InvariantCulture) : 0.0;
    }
}
