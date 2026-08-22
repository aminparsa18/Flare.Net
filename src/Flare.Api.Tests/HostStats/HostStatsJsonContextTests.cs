using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.HostStats;

/// <summary>Round-trip + wire-shape tests for <see cref="HostStatsJsonContext"/> - camelCase properties, matching every other outbound <c>*JsonContext</c> in this project (see <c>DockerResources.ResourceGraphJsonContextTests</c>).</summary>
public class HostStatsJsonContextTests
{
    [Fact]
    public void RoundTrips_Snapshot_WithAllFieldsPopulated()
    {
        var original = new HostStatsSnapshot
        {
            Available = true,
            UnavailableReason = null,
            CpuUsagePercent = 23.4,
            CpuCoreCount = 8,
            LoadAverage1m = 1.42,
            PerCoreUsagePercent = [12.0, 34.5, 20.1, 15.0, 40.2, 8.8, 19.9, 30.0],
            MemoryTotalBytes = 16_000_000_000,
            MemoryUsedBytes = 6_200_000_000,
            MemoryAvailableBytes = 9_800_000_000,
            SwapTotalBytes = 2_000_000_000,
            SwapUsedBytes = 100_000_000,
            DiskTotalBytes = 100_000_000_000,
            DiskUsedBytes = 42_000_000_000,
            DiskAvailableBytes = 58_000_000_000,
            DiskGrowthBytesPerDay = 1_200_000_000,
            DiskGrowthWindowHours = 6.5,
            DiskReadBytesPerSecond = 4_100_000,
            DiskWriteBytesPerSecond = 1_100_000,
            NetworkBytesPerSecond = 1_250_000.5,
            NetworkRxBytesPerSecond = 800_000,
            NetworkTxBytesPerSecond = 450_000.5,
            NetworkPacketsPerSecond = 340,
            UptimeSeconds = 12345.67,
            UpdatedAt = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero),
        };

        var json = JsonSerializer.Serialize(original, HostStatsJsonContext.Default.HostStatsSnapshot);
        var roundTripped = JsonSerializer.Deserialize(json, HostStatsJsonContext.Default.HostStatsSnapshot);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.Available, roundTripped.Available);
        Assert.Equal(original.CpuUsagePercent, roundTripped.CpuUsagePercent);
        Assert.Equal(original.CpuCoreCount, roundTripped.CpuCoreCount);
        Assert.Equal(original.LoadAverage1m, roundTripped.LoadAverage1m);
        Assert.Equal(original.PerCoreUsagePercent, roundTripped.PerCoreUsagePercent);
        Assert.Equal(original.MemoryTotalBytes, roundTripped.MemoryTotalBytes);
        Assert.Equal(original.MemoryUsedBytes, roundTripped.MemoryUsedBytes);
        Assert.Equal(original.MemoryAvailableBytes, roundTripped.MemoryAvailableBytes);
        Assert.Equal(original.SwapTotalBytes, roundTripped.SwapTotalBytes);
        Assert.Equal(original.SwapUsedBytes, roundTripped.SwapUsedBytes);
        Assert.Equal(original.DiskTotalBytes, roundTripped.DiskTotalBytes);
        Assert.Equal(original.DiskUsedBytes, roundTripped.DiskUsedBytes);
        Assert.Equal(original.DiskAvailableBytes, roundTripped.DiskAvailableBytes);
        Assert.Equal(original.DiskGrowthBytesPerDay, roundTripped.DiskGrowthBytesPerDay);
        Assert.Equal(original.DiskGrowthWindowHours, roundTripped.DiskGrowthWindowHours);
        Assert.Equal(original.DiskReadBytesPerSecond, roundTripped.DiskReadBytesPerSecond);
        Assert.Equal(original.DiskWriteBytesPerSecond, roundTripped.DiskWriteBytesPerSecond);
        Assert.Equal(original.NetworkBytesPerSecond, roundTripped.NetworkBytesPerSecond);
        Assert.Equal(original.NetworkRxBytesPerSecond, roundTripped.NetworkRxBytesPerSecond);
        Assert.Equal(original.NetworkTxBytesPerSecond, roundTripped.NetworkTxBytesPerSecond);
        Assert.Equal(original.NetworkPacketsPerSecond, roundTripped.NetworkPacketsPerSecond);
        Assert.Equal(original.UptimeSeconds, roundTripped.UptimeSeconds);
        Assert.Equal(original.UpdatedAt, roundTripped.UpdatedAt);
    }

    [Fact]
    public void RoundTrips_Snapshot_WithDiskGrowthNull_WhenNotEnoughSpanYet()
    {
        var original = new HostStatsSnapshot { Available = true, DiskGrowthBytesPerDay = null, DiskGrowthWindowHours = 0.5 };

        var json = JsonSerializer.Serialize(original, HostStatsJsonContext.Default.HostStatsSnapshot);
        var roundTripped = JsonSerializer.Deserialize(json, HostStatsJsonContext.Default.HostStatsSnapshot);

        Assert.NotNull(roundTripped);
        Assert.Null(roundTripped.DiskGrowthBytesPerDay);
        Assert.Equal(original.DiskGrowthWindowHours, roundTripped.DiskGrowthWindowHours);
    }

    [Fact]
    public void RoundTrips_HistoryPointArray()
    {
        IReadOnlyList<HostStatsHistoryPoint> original =
        [
            new HostStatsHistoryPoint
            {
                Timestamp = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero),
                CpuUsagePercent = 12.5,
                MemoryUsedPercent = 38.8,
                DiskUsedPercent = 42.0,
                NetworkBytesPerSecond = 1_500_000,
            },
            new HostStatsHistoryPoint
            {
                Timestamp = new DateTimeOffset(2026, 8, 22, 12, 0, 2, TimeSpan.Zero),
                CpuUsagePercent = 14.1,
                MemoryUsedPercent = 39.0,
                DiskUsedPercent = 42.0,
                NetworkBytesPerSecond = 1_800_000,
            },
        ];

        var json = JsonSerializer.Serialize(original, HostStatsJsonContext.Default.IReadOnlyListHostStatsHistoryPoint);
        var roundTripped = JsonSerializer.Deserialize(json, HostStatsJsonContext.Default.IReadOnlyListHostStatsHistoryPoint);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.Count, roundTripped.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Timestamp, roundTripped[i].Timestamp);
            Assert.Equal(original[i].CpuUsagePercent, roundTripped[i].CpuUsagePercent);
            Assert.Equal(original[i].MemoryUsedPercent, roundTripped[i].MemoryUsedPercent);
            Assert.Equal(original[i].DiskUsedPercent, roundTripped[i].DiskUsedPercent);
            Assert.Equal(original[i].NetworkBytesPerSecond, roundTripped[i].NetworkBytesPerSecond);
        }
    }

    [Fact]
    public void Serializes_NotAvailableSnapshot_WithCamelCaseProperties()
    {
        var snapshot = new HostStatsSnapshot
        {
            Available = false,
            UnavailableReason = "Host stats need Linux's /proc filesystem.",
        };

        var json = JsonSerializer.Serialize(snapshot, HostStatsJsonContext.Default.HostStatsSnapshot);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("available").GetBoolean());
        Assert.Equal("Host stats need Linux's /proc filesystem.", root.GetProperty("unavailableReason").GetString());
        Assert.Equal(0, root.GetProperty("cpuUsagePercent").GetDouble());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("updatedAt").ValueKind);
    }
}
