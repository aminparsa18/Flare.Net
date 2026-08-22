using System.Globalization;
using System.Text.RegularExpressions;

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
/// <c>/proc/loadavg</c>/<c>/proc/net/dev</c>/<c>/proc/diskstats</c>, not a cgroup-scoped
/// view - which is exactly what makes this feature work from inside Flare.Api's own
/// container without any extra plumbing (compare <see cref="HostDiskReader"/>, where disk
/// *space* genuinely does need a bind mount - disk *I/O*, read here, doesn't).
/// </remarks>
internal static partial class ProcFileReader
{
    /// <summary>One <c>/proc/stat</c> CPU line's idle/total tick counts. <see cref="CoreCount"/> is only meaningful on the result of <see cref="ParseStat"/> (the aggregate line, counting the <c>cpuN</c> lines that follow it) - 0 and unused on each individual <see cref="ParseStatPerCore"/> entry.</summary>
    internal readonly record struct CpuSample(long IdleTicks, long TotalTicks, int CoreCount);

    internal readonly record struct MemInfo(long TotalBytes, long AvailableBytes, long SwapTotalBytes, long SwapFreeBytes);

    /// <summary>One <c>/proc/net/dev</c> sample, already summed across every non-loopback interface - see <see cref="ParseNetDev"/>.</summary>
    internal readonly record struct NetSample(long RxBytes, long TxBytes, long RxPackets, long TxPackets);

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

            (idle, total) = ParseCpuTicks(fields);
        }

        return new CpuSample(idle, total, coreCount);
    }

    /// <summary>
    /// Per-core CPU samples - one <see cref="CpuSample"/> per <c>cpuN</c> line, in core
    /// order, same tick parsing <see cref="ParseStat"/>'s aggregate line uses. Feeds the
    /// Host overview panel's per-core breakdown - <see cref="HostStatsPoller"/> diffs each
    /// core's sample against its own previous tick the same way
    /// <see cref="CalculateCpuUsagePercent"/> diffs the aggregate.
    /// </summary>
    internal static IReadOnlyList<CpuSample> ParseStatPerCore(string procStatText)
    {
        var perCore = new List<CpuSample>();

        foreach (var rawLine in procStatText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (!line.StartsWith("cpu", StringComparison.Ordinal) || line.Length <= 3 || !char.IsDigit(line[3]))
            {
                continue;
            }

            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5)
            {
                continue;
            }

            var (idle, total) = ParseCpuTicks(fields);
            perCore.Add(new CpuSample(idle, total, CoreCount: 0));
        }

        return perCore;
    }

    /// <summary>Sums a <c>cpuN ...</c>-shaped line's tick fields (fields[0] is the "cpuN" label itself) into idle/total - shared by <see cref="ParseStat"/>'s aggregate line and <see cref="ParseStatPerCore"/>'s per-core lines, same field layout either way.</summary>
    private static (long Idle, long Total) ParseCpuTicks(string[] fieldsIncludingLabel)
    {
        var ticks = new long[fieldsIncludingLabel.Length - 1];
        for (var i = 1; i < fieldsIncludingLabel.Length; i++)
        {
            ticks[i - 1] = long.Parse(fieldsIncludingLabel[i], CultureInfo.InvariantCulture);
        }

        // idle + iowait, same convention top/htop use - iowait is still "not busy" time
        // even though the kernel accounts it separately from idle.
        var idle = ticks[3] + (ticks.Length > 4 ? ticks[4] : 0);
        long total = 0;
        foreach (var tick in ticks)
        {
            total += tick;
        }

        return (idle, total);
    }

    /// <summary>
    /// CPU busy percentage between two <see cref="ParseStat"/>/<see cref="ParseStatPerCore"/>
    /// samples taken <see cref="HostStatsOptions.PollDelay"/> apart - the standard delta
    /// technique (<c>busy_delta / total_delta</c>) <c>/proc/stat</c>'s cumulative-since-boot
    /// tick counters require, same as <c>top</c>/<c>htop</c>/<c>docker stats</c>. Zero total
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

    /// <summary>Parses <c>MemTotal</c>/<c>MemAvailable</c>/<c>SwapTotal</c>/<c>SwapFree</c> (kB, converted to bytes). <c>MemAvailable</c> - not the smaller/cruder <c>MemFree</c> - is what <c>free -h</c>'s own "available" column reports too: free memory plus reclaimable cache/buffers, i.e. what's actually usable. Swap fields are simply absent (both fields land on their zero default) on a host with swap disabled entirely - common under Docker Desktop and various cloud VM images - not an error case.</summary>
    internal static MemInfo ParseMemInfo(string procMemInfoText)
    {
        long totalKb = 0;
        long availableKb = 0;
        long swapTotalKb = 0;
        long swapFreeKb = 0;

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
            else if (line.StartsWith("SwapTotal:", StringComparison.Ordinal))
            {
                swapTotalKb = ParseKbField(line);
            }
            else if (line.StartsWith("SwapFree:", StringComparison.Ordinal))
            {
                swapFreeKb = ParseKbField(line);
            }
        }

        return new MemInfo(totalKb * 1024, availableKb * 1024, swapTotalKb * 1024, swapFreeKb * 1024);
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

    /// <summary>
    /// Sums received/transmitted bytes and packets across every interface in
    /// <c>/proc/net/dev</c> except <c>lo</c> (the loopback interface - traffic that never
    /// actually leaves the host, so counting it would inflate "network activity" with e.g.
    /// this same process's own localhost calls). Format: two header lines, then one line
    /// per interface - <c>iface: rx_bytes rx_packets rx_errs rx_drop rx_fifo rx_frame
    /// rx_compressed rx_multicast tx_bytes tx_packets tx_errs tx_drop tx_fifo tx_colls
    /// tx_carrier tx_compressed</c> (16 fields after the <c>:</c>). This is a point-in-time
    /// cumulative-since-boot total, same as <see cref="ParseStat"/>'s tick counters -
    /// <see cref="HostStatsPoller"/> diffs two samples against the *actual* elapsed wall-
    /// clock time between them to get a rate (unlike CPU%, a byte/packet delta has no tick-
    /// count of its own to divide by).
    /// </summary>
    internal static NetSample ParseNetDev(string procNetDevText)
    {
        long rxBytes = 0, txBytes = 0, rxPackets = 0, txPackets = 0;

        foreach (var rawLine in procNetDevText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').TrimStart();
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue; // header lines, or a malformed line - neither has a leading "iface:".
            }

            var iface = line[..colon];
            if (iface == "lo")
            {
                continue;
            }

            var fields = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 10)
            {
                continue;
            }

            rxBytes += long.Parse(fields[0], CultureInfo.InvariantCulture);
            rxPackets += long.Parse(fields[1], CultureInfo.InvariantCulture);
            txBytes += long.Parse(fields[8], CultureInfo.InvariantCulture);
            txPackets += long.Parse(fields[9], CultureInfo.InvariantCulture);
        }

        return new NetSample(rxBytes, txBytes, rxPackets, txPackets);
    }

    // Partition suffix for nvme/mmcblk-style whole-device names, which legitimately end in
    // a digit themselves (nvme0n1, mmcblk0) - a partition adds a further "p<digit>"
    // (nvme0n1p1, mmcblk0p1), which plain sd*/vd*/xvd*/hd* names never do (sda's partition
    // is sda1, not sdap1).
    [GeneratedRegex(@"^(nvme\d+n\d+|mmcblk\d+)p\d+$")]
    private static partial Regex NvmeOrMmcPartitionPattern();

    /// <summary>
    /// Best-effort "is this <c>/proc/diskstats</c> device name a whole disk, not a
    /// partition or a virtual/pseudo device" - <c>/proc/diskstats</c> lists every block
    /// device the kernel knows about, partitions included (<c>sda</c> *and* <c>sda1</c>),
    /// so summing every line would double-count real I/O. Not exact - an unusual naming
    /// scheme could misclassify - but covers the common device families:
    /// <c>loop*</c>/<c>dm-*</c>/<c>ram*</c>/<c>sr*</c> (virtual/pseudo, never real host
    /// I/O) are excluded outright; <c>nvme\d+n\d+</c>/<c>mmcblk\d+</c> whole-device names
    /// are kept unless a trailing <c>p\d+</c> partition suffix follows; everything else
    /// (<c>sd*</c>/<c>vd*</c>/<c>xvd*</c>/<c>hd*</c> and unrecognized names alike) is kept
    /// unless the name itself ends in a digit, which for those families only a partition
    /// number ever does.
    /// </summary>
    internal static bool IsWholeDiskDevice(string deviceName)
    {
        if (deviceName.StartsWith("loop", StringComparison.Ordinal) ||
            deviceName.StartsWith("dm-", StringComparison.Ordinal) ||
            deviceName.StartsWith("ram", StringComparison.Ordinal) ||
            deviceName.StartsWith("sr", StringComparison.Ordinal))
        {
            return false;
        }

        return !NvmeOrMmcPartitionPattern().IsMatch(deviceName) &&
            (deviceName.Length == 0 || !char.IsDigit(deviceName[^1]) || IsNvmeOrMmcWholeDevice(deviceName));
    }

    [GeneratedRegex(@"^(nvme\d+n\d+|mmcblk\d+)$")]
    private static partial Regex NvmeOrMmcWholeDevicePattern();

    private static bool IsNvmeOrMmcWholeDevice(string deviceName) => NvmeOrMmcWholeDevicePattern().IsMatch(deviceName);

    /// <summary>One <c>/proc/diskstats</c> sample - see <see cref="ParseDiskStats"/>.</summary>
    internal readonly record struct DiskIoSample(long ReadBytes, long WriteBytes);

    /// <summary>
    /// Sums <c>sectors_read</c> and <c>sectors_written</c> separately (field indices 5 and
    /// 9 of the 14+ whitespace-separated fields after <c>major minor name</c>) across every
    /// whole-disk device (see <see cref="IsWholeDiskDevice"/>), converted from 512-byte
    /// sectors to bytes - <c>/proc/diskstats</c>' sectors are always 512 bytes regardless
    /// of the device's real block size, a fixed kernel convention, not this device's actual
    /// sector size. Same cumulative-since-boot/delta-to-rate shape as
    /// <see cref="ParseNetDev"/>.
    /// </summary>
    internal static DiskIoSample ParseDiskStats(string procDiskStatsText)
    {
        const int SectorBytes = 512;
        long readSectors = 0;
        long writeSectors = 0;

        foreach (var rawLine in procDiskStatsText.Split('\n'))
        {
            var fields = rawLine.TrimEnd('\r').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 10)
            {
                continue;
            }

            var deviceName = fields[2];
            if (!IsWholeDiskDevice(deviceName))
            {
                continue;
            }

            readSectors += long.Parse(fields[5], CultureInfo.InvariantCulture); // sectors_read
            writeSectors += long.Parse(fields[9], CultureInfo.InvariantCulture); // sectors_written
        }

        return new DiskIoSample(readSectors * SectorBytes, writeSectors * SectorBytes);
    }
}
