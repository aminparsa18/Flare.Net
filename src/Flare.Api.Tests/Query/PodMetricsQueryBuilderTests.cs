using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class PodMetricsQueryBuilderTests
{
    private static readonly DateTimeOffset End = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildPodMetrics_ScopedToPodName_WithoutNamespace_MatchesAnyNamespace()
    {
        var result = PodMetricsQueryBuilder.BuildPodMetrics("web-0", null, 30, 60, End);
        var parameters = result.Parameters.ToDictionary();

        Assert.Contains("ResourceAttributes['k8s.pod.name'] = {podName:String}", result.Sql);
        Assert.DoesNotContain("k8s.namespace.name", result.Sql);
        Assert.Equal("web-0", parameters["podName"]);
        Assert.False(parameters.ContainsKey("podNamespace"));
    }

    [Fact]
    public void BuildPodMetrics_WithNamespace_NarrowsByIt()
    {
        var result = PodMetricsQueryBuilder.BuildPodMetrics("web-0", " shop ", 30, 60, End);

        Assert.Contains("ResourceAttributes['k8s.namespace.name'] = {podNamespace:String}", result.Sql);
        Assert.Equal("shop", result.Parameters.ToDictionary()["podNamespace"]);
    }

    [Fact]
    public void BuildPodMetrics_WindowEndsAtEnd_BucketedByWidth()
    {
        var result = PodMetricsQueryBuilder.BuildPodMetrics("web-0", null, 30, 60, End);
        var parameters = result.Parameters.ToDictionary();

        Assert.Equal(End.UtcDateTime, parameters["to"]);
        Assert.Equal(End.AddMinutes(-30).UtcDateTime, parameters["from"]);
        Assert.Equal(60u, parameters["bucketWidth"]);
        Assert.Contains("toStartOfInterval(Time, INTERVAL {bucketWidth:UInt32} SECOND) AS BucketStart", result.Sql);
    }

    [Fact]
    public void BuildPodMetrics_ReadsBothCpuMetricNames_AndScalesLimitRatiosToPercent()
    {
        var sql = PodMetricsQueryBuilder.BuildPodMetrics("web-0", null, 30, 60, End).Sql;

        Assert.Contains("MetricName IN ('k8s.pod.cpu.usage', 'k8s.pod.cpu.utilization'), 'cpu'", sql);
        Assert.Contains("MetricName = 'k8s.pod.memory.working_set', 'memory'", sql);
        Assert.Contains("if(endsWith(MetricName, '_limit_utilization'), 100 * Value, Value)", sql);
        Assert.Contains("FROM metrics_gauge", sql);
    }
}
