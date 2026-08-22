using Flare.Api.HostStats;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.HostStats;

/// <summary>
/// Tests for <see cref="HostStatsPoller"/>'s pure history-trimming logic - not the live
/// polling loop itself, same scope as <c>ProcFileReaderTests</c>/
/// <c>DockerResources.DockerContainerPollerTests</c>.
/// </summary>
public class HostStatsPollerTests
{
    private static HostStatsHistoryPoint MakePoint(DateTimeOffset timestamp) => new()
    {
        Timestamp = timestamp,
        CpuUsagePercent = 0,
        MemoryUsedPercent = 0,
        DiskUsedPercent = 0,
        NetworkBytesPerSecond = 0,
    };

    [Fact]
    public void TrimOlderThan_RemovesPoints_AtOrBeforeCutoff()
    {
        var baseline = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        var history = new List<HostStatsHistoryPoint>
        {
            MakePoint(baseline),
            MakePoint(baseline.AddMinutes(1)),
            MakePoint(baseline.AddMinutes(2)),
            MakePoint(baseline.AddMinutes(3)),
        };

        HostStatsPoller.TrimOlderThan(history, baseline.AddMinutes(1)); // cutoff == the 2nd point's own timestamp

        Assert.Equal(2, history.Count);
        Assert.Equal(baseline.AddMinutes(2), history[0].Timestamp);
        Assert.Equal(baseline.AddMinutes(3), history[1].Timestamp);
    }

    [Fact]
    public void TrimOlderThan_LeavesEverything_WhenNothingIsPastCutoff()
    {
        var baseline = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        var history = new List<HostStatsHistoryPoint> { MakePoint(baseline), MakePoint(baseline.AddMinutes(1)) };

        HostStatsPoller.TrimOlderThan(history, baseline.AddMinutes(-1));

        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void TrimOlderThan_RemovesEverything_WhenCutoffIsAfterTheNewestPoint()
    {
        var baseline = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        var history = new List<HostStatsHistoryPoint> { MakePoint(baseline), MakePoint(baseline.AddMinutes(1)) };

        HostStatsPoller.TrimOlderThan(history, baseline.AddHours(1));

        Assert.Empty(history);
    }

    private static readonly DateTimeOffset Baseline = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TrimDiskGrowthSamples_RemovesSamples_AtOrBeforeCutoff()
    {
        var samples = new List<HostStatsPoller.DiskGrowthSample>
        {
            new(Baseline, 1000),
            new(Baseline.AddHours(1), 1100),
            new(Baseline.AddHours(2), 1200),
        };

        HostStatsPoller.TrimDiskGrowthSamples(samples, Baseline.AddHours(1));

        Assert.Single(samples);
        Assert.Equal(Baseline.AddHours(2), samples[0].Timestamp);
    }

    [Fact]
    public void CalculateDiskGrowth_ReturnsNull_WhenBufferIsEmpty()
    {
        var (bytesPerDay, windowHours) = HostStatsPoller.CalculateDiskGrowth([], Baseline, currentUsedBytes: 1000, TimeSpan.FromHours(2));

        Assert.Null(bytesPerDay);
        Assert.Equal(0.0, windowHours);
    }

    [Fact]
    public void CalculateDiskGrowth_ReturnsNull_WhenSpanIsBelowMinimum()
    {
        var samples = new List<HostStatsPoller.DiskGrowthSample> { new(Baseline, 1_000_000_000) };
        var now = Baseline.AddHours(1); // 1h span, minimum is 2h

        var (bytesPerDay, windowHours) = HostStatsPoller.CalculateDiskGrowth(samples, now, currentUsedBytes: 1_100_000_000, TimeSpan.FromHours(2));

        Assert.Null(bytesPerDay);
        Assert.Equal(1.0, windowHours, precision: 2); // still reports how much span exists, just not a rate yet
    }

    [Fact]
    public void CalculateDiskGrowth_ScalesTheOldestToNewestDelta_ToAFullDay()
    {
        var samples = new List<HostStatsPoller.DiskGrowthSample> { new(Baseline, 1_000_000_000) };
        var now = Baseline.AddHours(6); // 6h span, half a quarter-day

        var (bytesPerDay, windowHours) = HostStatsPoller.CalculateDiskGrowth(samples, now, currentUsedBytes: 1_300_000_000, TimeSpan.FromHours(2));

        // +300,000,000 bytes over 6h -> *4 to scale a quarter-day span up to a full day.
        Assert.NotNull(bytesPerDay);
        Assert.Equal(1_200_000_000, bytesPerDay.Value, precision: 0);
        Assert.Equal(6.0, windowHours, precision: 2);
    }

    [Fact]
    public void CalculatePerCoreUsagePercent_ReturnsEmpty_WhenThereIsNoPreviousSample()
    {
        var current = ProcFileReader.ParseStatPerCore("cpu0 25 0 25 200 0 0 0 0 0 0");

        Assert.Empty(HostStatsPoller.CalculatePerCoreUsagePercent(null, current));
    }

    [Fact]
    public void CalculatePerCoreUsagePercent_ReturnsEmpty_WhenCoreCountsDoNotMatch()
    {
        var previous = ProcFileReader.ParseStatPerCore("cpu0 25 0 25 200 0 0 0 0 0 0");
        var current = ProcFileReader.ParseStatPerCore("""
            cpu0 50 0 50 400 0 0 0 0 0 0
            cpu1 50 0 50 400 0 0 0 0 0 0
            """);

        Assert.Empty(HostStatsPoller.CalculatePerCoreUsagePercent(previous, current));
    }

    [Fact]
    public void CalculatePerCoreUsagePercent_DiffsEachCoreIndependently()
    {
        var previous = ProcFileReader.ParseStatPerCore("""
            cpu0 100 0 100 800 0 0 0 0 0 0
            cpu1 100 0 100 800 0 0 0 0 0 0
            """);
        var current = ProcFileReader.ParseStatPerCore("""
            cpu0 150 0 150 850 0 0 0 0 0 0
            cpu1 100 0 100 900 0 0 0 0 0 0
            """);

        var percents = HostStatsPoller.CalculatePerCoreUsagePercent(previous, current);

        Assert.Equal(2, percents.Count);
        Assert.Equal(66.67, percents[0], precision: 2); // same delta as the aggregate CPU% test
        Assert.Equal(0.0, percents[1]); // idle-only delta - core1 did no work
    }

    [Fact]
    public void CalculateNetworkRates_ReturnsZero_WhenThereIsNoPreviousSample()
    {
        var current = new ProcFileReader.NetSample(1000, 500, 10, 5);

        var (rx, tx, packets) = HostStatsPoller.CalculateNetworkRates(null, current, Baseline);

        Assert.Equal(0.0, rx);
        Assert.Equal(0.0, tx);
        Assert.Equal(0.0, packets);
    }

    [Fact]
    public void CalculateNetworkRates_DividesByteAndPacketDeltas_ByActualElapsedTime()
    {
        var previous = (new ProcFileReader.NetSample(RxBytes: 1000, TxBytes: 500, RxPackets: 10, TxPackets: 5), Baseline);
        var current = new ProcFileReader.NetSample(RxBytes: 3000, TxBytes: 1700, RxPackets: 30, TxPackets: 15);

        var (rx, tx, packets) = HostStatsPoller.CalculateNetworkRates(previous, current, Baseline.AddSeconds(2));

        Assert.Equal(1000.0, rx); // (3000-1000)/2s
        Assert.Equal(600.0, tx); // (1700-500)/2s
        Assert.Equal(15.0, packets); // ((30-10)+(15-5))/2s
    }

    [Fact]
    public void CalculateDiskIoRates_DividesByteDeltas_ByActualElapsedTime()
    {
        var previous = (new ProcFileReader.DiskIoSample(ReadBytes: 10_000, WriteBytes: 4_000), Baseline);
        var current = new ProcFileReader.DiskIoSample(ReadBytes: 30_000, WriteBytes: 14_000);

        var (read, write) = HostStatsPoller.CalculateDiskIoRates(previous, current, Baseline.AddSeconds(4));

        Assert.Equal(5_000.0, read); // (30,000-10,000)/4s
        Assert.Equal(2_500.0, write); // (14,000-4,000)/4s
    }
}
