using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class HostInventoryQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, HostInventoryQueryBuilder.DefaultWindowMinutes)]
    [InlineData(0, HostInventoryQueryBuilder.DefaultWindowMinutes)]
    [InlineData(-5, HostInventoryQueryBuilder.DefaultWindowMinutes)]
    [InlineData(1, HostInventoryQueryBuilder.MinWindowMinutes)]
    [InlineData(30, 30)]
    [InlineData(100_000, HostInventoryQueryBuilder.MaxWindowMinutes)]
    public void ClampWindowMinutes_DefaultsAndClamps(int? requested, int expected)
    {
        Assert.Equal(expected, HostInventoryQueryBuilder.ClampWindowMinutes(requested));
    }

    [Theory]
    [InlineData(5, 60)]
    [InlineData(60, 60)]
    [InlineData(90, 120)]
    [InlineData(360, 360)]
    [InlineData(1440, 1440)]
    public void BucketWidthSecondsFor_TargetsAboutSixtyWholeMinuteBuckets_NeverUnderAMinute(int windowMinutes, int expected)
    {
        Assert.Equal(expected, HostInventoryQueryBuilder.BucketWidthSecondsFor(windowMinutes));
    }

    [Fact]
    public void BuildHostList_UnionsGaugeAndSum_OnSystemMetricsWithAHostName()
    {
        var result = HostInventoryQueryBuilder.BuildHostList(new HostListRequest(), 60, Now);

        Assert.Contains("FROM metrics_gauge", result.Sql);
        Assert.Contains("FROM metrics_sum", result.Sql);
        Assert.DoesNotContain("metrics_histogram", result.Sql);
        Assert.Equal(2, CountOccurrences(result.Sql, "MetricName LIKE 'system.%'"));
        Assert.Equal(2, CountOccurrences(result.Sql, "Host != ''"));
        Assert.Contains("ResourceAttributes['host.name'] AS Host", result.Sql);
    }

    [Fact]
    public void BuildHostList_BindsWindowAsTimeRange_AndFetchesOneRowPastTheCap()
    {
        var parameters = HostInventoryQueryBuilder.BuildHostList(new HostListRequest(), 60, Now).Parameters.ToDictionary();

        Assert.Equal(Now.AddMinutes(-60).UtcDateTime, parameters["from"]);
        Assert.Equal(Now.UtcDateTime, parameters["to"]);
        Assert.Equal((uint)(HostInventoryQueryBuilder.MaxHosts + 1), parameters["hostLimit"]);
    }

    [Fact]
    public void BuildHostList_NoFilters_BindsNoSearchOrOsType()
    {
        var result = HostInventoryQueryBuilder.BuildHostList(new HostListRequest { Search = "  ", OsType = "" }, 60, Now);

        Assert.DoesNotContain("{search:String}", result.Sql);
        Assert.DoesNotContain("{osType:String}", result.Sql);
    }

    [Fact]
    public void BuildHostList_SearchAndOsType_AppliedInBothBranches()
    {
        var result = HostInventoryQueryBuilder.BuildHostList(new HostListRequest { Search = " web ", OsType = "linux" }, 60, Now);
        var parameters = result.Parameters.ToDictionary();

        Assert.Equal(2, CountOccurrences(result.Sql, "positionCaseInsensitiveUTF8(Host, {search:String}) > 0"));
        Assert.Equal(2, CountOccurrences(result.Sql, "OsType = {osType:String}"));
        Assert.Equal("web", parameters["search"]);
        Assert.Equal("linux", parameters["osType"]);
    }

    [Fact]
    public void BuildListValues_HasOneBranchPerKind_KeyedByHost()
    {
        var result = HostInventoryQueryBuilder.BuildListValues(["a", "b"], 60, Now);

        Assert.Equal(3, CountOccurrences(result.Sql, "UNION ALL"));
        Assert.Equal(4, CountOccurrences(result.Sql, "SELECT Host AS Key"));
        Assert.Contains($"'{HostInventoryQueryBuilder.CpuKind}' AS Kind", result.Sql);
        Assert.Contains($"'{HostInventoryQueryBuilder.MemoryKind}' AS Kind", result.Sql);
        Assert.Contains($"'{HostInventoryQueryBuilder.DiskKind}' AS Kind", result.Sql);
        Assert.Contains($"'{HostInventoryQueryBuilder.LoadKind}' AS Kind", result.Sql);
        Assert.Equal(new[] { "a", "b" }, result.Parameters.ToDictionary()["hosts"]);
        Assert.Equal(4, CountOccurrences(result.Sql, "ResourceAttributes['host.name'] IN {hosts:Array(String)}"));
    }

    [Fact]
    public void BuildListValues_ReadsTheDefaultEnabledHostmetricsMetrics()
    {
        var sql = HostInventoryQueryBuilder.BuildListValues(["a"], 60, Now).Sql;

        Assert.Contains("MetricName = 'system.cpu.time'", sql);
        Assert.Contains("MetricName = 'system.memory.usage'", sql);
        Assert.Contains("MetricName = 'system.filesystem.usage'", sql);
        Assert.Contains("MetricName = 'system.cpu.load_average.15m'", sql);
    }

    [Fact]
    public void BuildListValues_Cpu_IsResetAwareNonIdleShareOfCounterIncrease()
    {
        var sql = HostInventoryQueryBuilder.BuildListValues(["a"], 60, Now).Sql;

        // Same per-row classification as MetricSeriesQueryBuilder's Sum shape (ADR-0044),
        // partitioned per counter (host + service + full attribute map).
        Assert.Contains("lagInFrame(Value) OVER w", sql);
        Assert.Contains("PARTITION BY ResourceAttributes['host.name'], ServiceName, toString(DataPointAttributes) ORDER BY Time", sql);
        Assert.Contains("SeriesRowNum = 1, 0", sql);
        Assert.Contains("RawDelta < 0, Value", sql);
        Assert.Contains("100 * sumIf(Delta, State != 'idle') / nullIf(sum(Delta), 0)", sql);
    }

    [Fact]
    public void BuildListValues_Memory_ExcludesSlabStatesFromTheTotal()
    {
        var sql = HostInventoryQueryBuilder.BuildListValues(["a"], 60, Now).Sql;

        Assert.Contains("NOT IN ('slab_reclaimable', 'slab_unreclaimable')", sql);
    }

    [Fact]
    public void BuildListValues_EveryBranchCastsToTheSameNullableType()
    {
        var sql = HostInventoryQueryBuilder.BuildListValues(["a"], 60, Now).Sql;

        Assert.Equal(4, CountOccurrences(sql, "AS Nullable(Float64)) AS Value"));
    }

    [Fact]
    public void BuildHostMetrics_KeyedByBucket_ScopedToOneHost()
    {
        var result = HostInventoryQueryBuilder.BuildHostMetrics("web-1", 60, 120, Now);
        var parameters = result.Parameters.ToDictionary();

        Assert.Equal(4, CountOccurrences(result.Sql, "SELECT toStartOfInterval(Time, INTERVAL {bucketWidth:UInt32} SECOND) AS Key"));
        Assert.Equal(4, CountOccurrences(result.Sql, "ResourceAttributes['host.name'] = {hostName:String}"));
        Assert.DoesNotContain("{hosts:Array(String)}", result.Sql);
        Assert.Equal("web-1", parameters["hostName"]);
        Assert.Equal(120u, parameters["bucketWidth"]);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
