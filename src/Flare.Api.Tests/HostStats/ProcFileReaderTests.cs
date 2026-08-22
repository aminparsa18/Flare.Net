using Flare.Api.HostStats;
using Xunit;

namespace Flare.Api.Tests.HostStats;

/// <summary>
/// Tests for <see cref="ProcFileReader"/>'s pure <c>/proc</c>-text parsing - fixture
/// strings shaped like the real files, no actual filesystem involved. Same scope as
/// <c>DockerResources.DockerContainerPollerTests</c>: pure mapping logic only, not
/// <see cref="HostStatsPoller"/>'s background loop.
/// </summary>
public class ProcFileReaderTests
{
    private const string FourCoreProcStat = """
        cpu  100 0 100 800 0 0 0 0 0 0
        cpu0 25 0 25 200 0 0 0 0 0 0
        cpu1 25 0 25 200 0 0 0 0 0 0
        cpu2 25 0 25 200 0 0 0 0 0 0
        cpu3 25 0 25 200 0 0 0 0 0 0
        intr 1462898744 10 0 0
        ctxt 1990473526
        btime 1062191376
        processes 2915
        """;

    [Fact]
    public void ParseStat_ReadsAggregateCpuLineTicksAndCountsPerCoreLines()
    {
        var sample = ProcFileReader.ParseStat(FourCoreProcStat);

        Assert.Equal(800, sample.IdleTicks); // idle(800) + iowait(0)
        Assert.Equal(1000, sample.TotalTicks); // sum of all ten tick fields
        Assert.Equal(4, sample.CoreCount); // cpu0..cpu3, not the "intr"/"ctxt"/"btime"/"processes" lines
    }

    [Fact]
    public void ParseStatPerCore_ReadsOneSamplePerCoreLine_InOrder()
    {
        var perCore = ProcFileReader.ParseStatPerCore(FourCoreProcStat);

        Assert.Equal(4, perCore.Count);
        foreach (var core in perCore)
        {
            Assert.Equal(200, core.IdleTicks); // idle(200) + iowait(0), each core line
            Assert.Equal(250, core.TotalTicks); // sum of that core's ten tick fields
        }
    }

    [Fact]
    public void CalculateCpuUsagePercent_ComputesBusyDeltaOverTotalDelta_BetweenTwoSamples()
    {
        var previous = ProcFileReader.ParseStat("cpu  100 0 100 800 0 0 0 0 0 0");
        var current = ProcFileReader.ParseStat("cpu  150 0 150 850 0 0 0 0 0 0");

        var percent = ProcFileReader.CalculateCpuUsagePercent(previous, current);

        // total delta 150, idle delta 50, busy delta 100 -> 100/150 = 66.67%
        Assert.Equal(66.67, percent, precision: 2);
    }

    [Fact]
    public void CalculateCpuUsagePercent_ReturnsZero_WhenTotalDeltaIsZeroOrNegative()
    {
        var sample = ProcFileReader.ParseStat("cpu  100 0 100 800 0 0 0 0 0 0");

        Assert.Equal(0.0, ProcFileReader.CalculateCpuUsagePercent(sample, sample));
    }

    [Fact]
    public void ParseMemInfo_ReadsTotalAvailableAndSwap_ConvertedFromKbToBytes()
    {
        const string procMemInfo = """
            MemTotal:       16337408 kB
            MemFree:          123456 kB
            MemAvailable:    6291456 kB
            Buffers:          200000 kB
            Cached:          3000000 kB
            SwapTotal:       2097152 kB
            SwapFree:        1048576 kB
            """;

        var memInfo = ProcFileReader.ParseMemInfo(procMemInfo);

        Assert.Equal(16337408L * 1024, memInfo.TotalBytes);
        Assert.Equal(6291456L * 1024, memInfo.AvailableBytes);
        Assert.Equal(2097152L * 1024, memInfo.SwapTotalBytes);
        Assert.Equal(1048576L * 1024, memInfo.SwapFreeBytes);
    }

    [Fact]
    public void ParseMemInfo_SwapFieldsDefaultToZero_WhenAbsent()
    {
        // Swap disabled entirely - common under Docker Desktop and various cloud VM images
        // - SwapTotal/SwapFree simply aren't present, not present-but-zero.
        const string procMemInfo = """
            MemTotal:       16337408 kB
            MemAvailable:    6291456 kB
            """;

        var memInfo = ProcFileReader.ParseMemInfo(procMemInfo);

        Assert.Equal(0, memInfo.SwapTotalBytes);
        Assert.Equal(0, memInfo.SwapFreeBytes);
    }

    [Fact]
    public void ParseUptimeSeconds_ReadsFirstField()
    {
        Assert.Equal(12345.67, ProcFileReader.ParseUptimeSeconds("12345.67 98765.43\n"), precision: 2);
    }

    [Fact]
    public void ParseLoadAverage1m_ReadsFirstField()
    {
        Assert.Equal(1.42, ProcFileReader.ParseLoadAverage1m("1.42 1.10 0.98 2/456 12345\n"), precision: 2);
    }

    [Fact]
    public void ParseNetDev_SumsRxTxBytesAndPackets_AcrossInterfaces_ExcludingLoopback()
    {
        const string procNetDev = """
            Inter-|   Receive                                                |  Transmit
             face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed
                lo:   12345     100    0    0    0     0          0         0    12345     100    0    0    0     0       0          0
              eth0: 1000000    5000    0    0    0     0          0         0   500000    3000    0    0    0     0       0          0
              eth1:  200000    1000    0    0    0     0          0         0   100000     500    0    0    0     0       0          0
            """;

        var sample = ProcFileReader.ParseNetDev(procNetDev);

        // eth0 + eth1 - lo excluded entirely.
        Assert.Equal(1_200_000, sample.RxBytes);
        Assert.Equal(600_000, sample.TxBytes);
        Assert.Equal(6_000, sample.RxPackets);
        Assert.Equal(3_500, sample.TxPackets);
    }

    [Theory]
    [InlineData("sda", true)]
    [InlineData("sda1", false)]
    [InlineData("vda", true)]
    [InlineData("vda2", false)]
    [InlineData("nvme0n1", true)]
    [InlineData("nvme0n1p1", false)]
    [InlineData("mmcblk0", true)]
    [InlineData("mmcblk0p1", false)]
    [InlineData("loop0", false)]
    [InlineData("dm-0", false)]
    [InlineData("ram0", false)]
    [InlineData("sr0", false)]
    public void IsWholeDiskDevice_DistinguishesWholeDisksFromPartitionsAndVirtualDevices(string deviceName, bool expected)
    {
        Assert.Equal(expected, ProcFileReader.IsWholeDiskDevice(deviceName));
    }

    [Fact]
    public void ParseDiskStats_SumsReadAndWriteSectors_AcrossWholeDisksOnly_ConvertedToBytes()
    {
        const string procDiskStats = """
               8       0 sda 100 5 2000 10 200 5 4000 20 0 5 30
               8       1 sda1 50 2 500 5 80 2 800 8 0 2 10
               7       0 loop0 10 0 999 1 10 0 999 1 0 1 2
            """;

        var sample = ProcFileReader.ParseDiskStats(procDiskStats);

        // sda only (sectors_read 2000 + sectors_written 4000 = 6000) * 512 bytes/sector -
        // sda1 (partition) and loop0 (virtual) both excluded.
        Assert.Equal(6000L * 512, sample.ReadBytes + sample.WriteBytes);
        Assert.Equal(2000L * 512, sample.ReadBytes);
        Assert.Equal(4000L * 512, sample.WriteBytes);
    }
}
