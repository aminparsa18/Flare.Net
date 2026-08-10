using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class MetricNamesQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_UnionsAllThreeTables_WithTheirOwnTypeDiscriminator()
    {
        var result = MetricNamesQueryBuilder.Build(new MetricNamesRequest(), Now);

        Assert.Contains("FROM metrics_gauge", result.Sql);
        Assert.Contains("'gauge' AS Type", result.Sql);
        Assert.Contains("FROM metrics_sum", result.Sql);
        Assert.Contains("'sum' AS Type", result.Sql);
        Assert.Contains("FROM metrics_histogram", result.Sql);
        Assert.Contains("'histogram' AS Type", result.Sql);
        Assert.Equal(2, CountOccurrences(result.Sql, "UNION ALL"));
    }

    [Fact]
    public void Build_GroupsByMetricNameAndServiceName_PerTable()
    {
        var result = MetricNamesQueryBuilder.Build(new MetricNamesRequest(), Now);

        // Not MetricName alone - the same metric name can be emitted by more than one
        // service (see the builder's remarks), so ServiceName is part of the group key
        // too, in every branch.
        Assert.Equal(3, CountOccurrences(result.Sql, "GROUP BY MetricName, ServiceName"));
    }

    [Fact]
    public void Build_SelectsServiceName_PerTable()
    {
        var result = MetricNamesQueryBuilder.Build(new MetricNamesRequest(), Now);

        Assert.Equal(3, CountOccurrences(result.Sql, "SELECT MetricName, ServiceName,"));
    }

    [Fact]
    public void Build_OrdersByMetricNameThenServiceName()
    {
        var result = MetricNamesQueryBuilder.Build(new MetricNamesRequest(), Now);

        Assert.EndsWith("ORDER BY MetricName, ServiceName", result.Sql);
    }

    [Fact]
    public void Build_ReusesTimeAndServiceFilter_AcrossAllThreeBranches()
    {
        var result = MetricNamesQueryBuilder.Build(
            new MetricNamesRequest { Services = ["payments-api"] }, Now);

        Assert.Equal(3, CountOccurrences(result.Sql, "ServiceName IN {services:Array(String)}"));
        // Bound once, not once per branch - same named parameter reused across all three
        // UNION ALL SELECTs.
        Assert.Equal(["payments-api"], (string[])result.Parameters.ToDictionary()["services"]!);
    }

    [Fact]
    public void Build_WithExplicitFromTo_BindsThem()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

        var result = MetricNamesQueryBuilder.Build(new MetricNamesRequest { From = from, To = to }, Now);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(from.UtcDateTime, parameters["from"]);
        Assert.Equal(to.UtcDateTime, parameters["to"]);
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
