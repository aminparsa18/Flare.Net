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
            MemoryTotalBytes = 16_000_000_000,
            MemoryUsedBytes = 6_200_000_000,
            DiskTotalBytes = 100_000_000_000,
            DiskUsedBytes = 42_000_000_000,
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
        Assert.Equal(original.MemoryTotalBytes, roundTripped.MemoryTotalBytes);
        Assert.Equal(original.MemoryUsedBytes, roundTripped.MemoryUsedBytes);
        Assert.Equal(original.DiskTotalBytes, roundTripped.DiskTotalBytes);
        Assert.Equal(original.DiskUsedBytes, roundTripped.DiskUsedBytes);
        Assert.Equal(original.UptimeSeconds, roundTripped.UptimeSeconds);
        Assert.Equal(original.UpdatedAt, roundTripped.UpdatedAt);
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
