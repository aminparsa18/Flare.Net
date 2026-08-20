using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class MetricAttributeKeysQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_Gauge_SelectsFromGaugeTable()
    {
        var result = MetricAttributeKeysQueryBuilder.Build(
            new MetricAttributeKeysRequest { MetricName = "process.threads", Type = MetricPointType.Gauge }, Now);

        Assert.Contains("FROM metrics_gauge", result.Sql);
    }

    [Fact]
    public void Build_Sum_SelectsFromSumTable()
    {
        var result = MetricAttributeKeysQueryBuilder.Build(
            new MetricAttributeKeysRequest { MetricName = "dotnet.exceptions", Type = MetricPointType.Sum }, Now);

        Assert.Contains("FROM metrics_sum", result.Sql);
    }

    [Fact]
    public void Build_Histogram_SelectsFromHistogramTable()
    {
        var result = MetricAttributeKeysQueryBuilder.Build(
            new MetricAttributeKeysRequest { MetricName = "http.server.request.duration", Type = MetricPointType.Histogram }, Now);

        Assert.Contains("FROM metrics_histogram", result.Sql);
    }

    [Fact]
    public void Build_EnumeratesKeysViaArrayJoinMapKeys_WithDistinctValueCount_GroupedAndOrderedByKey()
    {
        var result = MetricAttributeKeysQueryBuilder.Build(
            new MetricAttributeKeysRequest { MetricName = "dotnet.exceptions", Type = MetricPointType.Sum }, Now);

        Assert.Contains("arrayJoin(mapKeys(DataPointAttributes)) AS Key", result.Sql);
        Assert.Contains("count(DISTINCT DataPointAttributes[Key]) AS DistinctValueCount", result.Sql);
        Assert.Contains("GROUP BY Key", result.Sql);
        Assert.EndsWith("ORDER BY Key", result.Sql);
    }

    [Fact]
    public void Build_BindsMetricName()
    {
        var result = MetricAttributeKeysQueryBuilder.Build(
            new MetricAttributeKeysRequest { MetricName = "dotnet.exceptions", Type = MetricPointType.Sum }, Now);

        Assert.Contains("WHERE MetricName = {metricName:String} AND", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("dotnet.exceptions", parameters["metricName"]);
    }

    [Fact]
    public void Build_AppliesFilter_ServicesAndAttributes()
    {
        var result = MetricAttributeKeysQueryBuilder.Build(
            new MetricAttributeKeysRequest
            {
                MetricName = "dotnet.exceptions",
                Type = MetricPointType.Sum,
                Filter = new MetricFilter
                {
                    Services = ["payments-api"],
                    Attributes = [new MetricAttributeFilter { Key = "state", Value = "active" }],
                },
            },
            Now);

        Assert.Contains("ServiceName IN {services:Array(String)}", result.Sql);
        Assert.Contains("DataPointAttributes[{attrKey0:String}] = {attrValue0:String}", result.Sql);
    }

    [Fact]
    public void Build_WithNullFilter_DoesNotThrow()
    {
        // Same System.Text.Json init-only-property caveat MetricSeriesQueryBuilderTests
        // guards against.
        var result = MetricAttributeKeysQueryBuilder.Build(
            new MetricAttributeKeysRequest { MetricName = "process.threads", Type = MetricPointType.Gauge, Filter = null! }, Now);

        Assert.Contains("WHERE MetricName = {metricName:String} AND Time >=", result.Sql);
    }
}
