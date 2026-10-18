using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class MetricExemplarQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(MetricPointType.Gauge, "gauge")]
    [InlineData(MetricPointType.Sum, "sum")]
    [InlineData(MetricPointType.Histogram, "histogram")]
    [InlineData(MetricPointType.ExponentialHistogram, "exponential_histogram")]
    public void Build_ScopesToTheMetricAndPointType(MetricPointType type, string label)
    {
        var built = MetricExemplarQueryBuilder.Build(
            new MetricExemplarsRequest { MetricName = "http.server.request.duration", Type = type }, Now);

        Assert.Contains("FROM metric_exemplars", built.Sql);
        Assert.Contains("MetricName = {metricName:String} AND PointType = {pointType:String}", built.Sql);
        Assert.Contains("ORDER BY Time DESC", built.Sql);
        Assert.Equal(label, MetricExemplarQueryBuilder.PointType(type));
    }

    [Fact]
    public void Build_AppliesTheSharedMetricFilter_AndClampsTheLimit()
    {
        var built = MetricExemplarQueryBuilder.Build(
            new MetricExemplarsRequest
            {
                MetricName = "m",
                Type = MetricPointType.Histogram,
                Filter = new MetricFilter
                {
                    Services = ["api"],
                    Attributes = [new MetricAttributeFilter { Key = "http.route", Value = "/a" }],
                },
                Limit = 1_000_000,
            },
            Now);

        Assert.Contains("ServiceName IN {services:Array(String)}", built.Sql);
        Assert.Contains("DataPointAttributes[{attrKey0:String}] = {attrValue0:String}", built.Sql);
        Assert.Contains("LIMIT {limit:UInt64}", built.Sql);
    }
}
