using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class RuntimeHealthQueryBuilderTests
{
    private static readonly DateTimeOffset End = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_ReadsCountersResetAwarePerInstance_AndQueueAsMax()
    {
        var result = RuntimeHealthQueryBuilder.Build("orders-api", 60, 60, End);

        Assert.Contains("MetricName IN {counterMetrics:Array(String)}", result.Sql);
        Assert.Contains("lagInFrame(Value) OVER w", result.Sql);
        Assert.Contains("RawDelta < 0, Value", result.Sql);
        Assert.Contains("PARTITION BY MetricName, multiIf(ResourceAttributes['service.instance.id']", result.Sql);
        Assert.Contains("max(Value) AS Value", result.Sql);
        Assert.Contains("MetricName = {queueMetric:String}", result.Sql);
        Assert.Contains("ServiceName IN {services:Array(String)}", result.Sql);
        Assert.Contains("LIMIT {limit:UInt32}", result.Sql);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(RuntimeHealthQueryBuilder.QueueLengthMetric, parameters["queueMetric"]);
        Assert.Equal((uint)60, parameters["bucketWidth"]);
        Assert.Equal(new[] { "orders-api" }, (string[])parameters["services"]!);
        Assert.Equal(RuntimeHealthQueryBuilder.CounterMetrics, (string[])parameters["counterMetrics"]!);
    }

    [Theory]
    [InlineData(5, 60)]
    [InlineData(60, 60)]
    [InlineData(360, 360)]
    [InlineData(1440, 1440)]
    public void BucketWidthSecondsFor_AimsAtSixtyBuckets_NeverUnderAMinute(int windowMinutes, int expected) =>
        Assert.Equal(expected, RuntimeHealthQueryBuilder.BucketWidthSecondsFor(windowMinutes));

    [Fact]
    public void ClampWindowMinutes_DefaultsAndClamps()
    {
        Assert.Equal(60, RuntimeHealthQueryBuilder.ClampWindowMinutes(null));
        Assert.Equal(60, RuntimeHealthQueryBuilder.ClampWindowMinutes(0));
        Assert.Equal(5, RuntimeHealthQueryBuilder.ClampWindowMinutes(1));
        Assert.Equal(1440, RuntimeHealthQueryBuilder.ClampWindowMinutes(99999));
    }
}
