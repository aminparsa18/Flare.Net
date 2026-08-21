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
    [Fact]
    public void ParseStat_ReadsAggregateCpuLineTicksAndCountsPerCoreLines()
    {
        const string procStat = """
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

        var sample = ProcFileReader.ParseStat(procStat);

        Assert.Equal(800, sample.IdleTicks); // idle(800) + iowait(0)
        Assert.Equal(1000, sample.TotalTicks); // sum of all ten tick fields
        Assert.Equal(4, sample.CoreCount); // cpu0..cpu3, not the "intr"/"ctxt"/"btime"/"processes" lines
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
    public void ParseMemInfo_ReadsTotalAndAvailable_ConvertedFromKbToBytes()
    {
        const string procMemInfo = """
            MemTotal:       16337408 kB
            MemFree:          123456 kB
            MemAvailable:    6291456 kB
            Buffers:          200000 kB
            Cached:          3000000 kB
            """;

        var memInfo = ProcFileReader.ParseMemInfo(procMemInfo);

        Assert.Equal(16337408L * 1024, memInfo.TotalBytes);
        Assert.Equal(6291456L * 1024, memInfo.AvailableBytes);
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
}
