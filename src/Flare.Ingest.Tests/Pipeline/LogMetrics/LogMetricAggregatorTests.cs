using Flare.Ingest.Model;
using Flare.Ingest.Pipeline.LogMetrics;
using Flare.Ingest.Pipeline.Rules;
using Xunit;

namespace Flare.Ingest.Tests.Pipeline.LogMetrics;

public class LogMetricAggregatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = T0.AddMinutes(1);

    [Fact]
    public void Aggregate_CountsOnlyMatchingEvents_AsOneDeltaMonotonicSum()
    {
        var events = new[] { Log(severity: 17), Log(severity: 17), Log(severity: 9) };
        var definition = Definition(condition: new PipelineRuleCondition { SeverityNumbers = [17] });

        var points = LogMetricAggregator.Aggregate(events, [definition], new LogMetricOptions(), Now);

        var point = Assert.Single(points);
        Assert.Equal("logs.errors", point.MetricName);
        Assert.Equal(2, point.Value);
        Assert.Equal(1, point.AggregationTemporality);
        Assert.True(point.IsMonotonic);
        Assert.Equal(Now, point.IngestedAt);
    }

    [Fact]
    public void Aggregate_SplitsByServiceAndTimeBucket()
    {
        var events = new[]
        {
            Log(service: "a", at: T0.AddSeconds(1)),
            Log(service: "a", at: T0.AddSeconds(9)),
            Log(service: "a", at: T0.AddSeconds(11)),
            Log(service: "b", at: T0.AddSeconds(1)),
        };

        var points = LogMetricAggregator.Aggregate(events, [Definition()], new LogMetricOptions(), Now);

        Assert.Equal(3, points.Count);
        Assert.Equal(2, points.Single(p => p.ServiceName == "a" && p.Time == T0).Value);
        Assert.Equal(1, points.Single(p => p.ServiceName == "a" && p.Time == T0.AddSeconds(10)).Value);
        Assert.Equal(1, points.Single(p => p.ServiceName == "b").Value);
    }

    [Fact]
    public void Aggregate_GroupsByAttribute_LogBagThenResourceBag_AndOmitsMissingValues()
    {
        var events = new[]
        {
            Log(logAttributes: new() { ["route"] = "/pay" }),
            Log(logAttributes: new() { ["route"] = "/pay" }),
            Log(resourceAttributes: new() { ["route"] = "/cart" }),
            Log(),
        };

        var points = LogMetricAggregator.Aggregate(events, [Definition(groupBy: ["route"])], new LogMetricOptions(), Now);

        Assert.Equal(3, points.Count);
        Assert.Equal(2, points.Single(p => p.DataPointAttributes.GetValueOrDefault("route") == "/pay").Value);
        Assert.Equal(1, points.Single(p => p.DataPointAttributes.GetValueOrDefault("route") == "/cart").Value);
        Assert.Equal(1, points.Single(p => p.DataPointAttributes.Count == 0).Value);
    }

    [Fact]
    public void Aggregate_PastMaxGroups_CountsNewCombinationsUnderOverflow_ButKeepsKnownOnes()
    {
        var events = new[]
        {
            Log(logAttributes: new() { ["id"] = "1" }),
            Log(logAttributes: new() { ["id"] = "2" }),
            Log(logAttributes: new() { ["id"] = "3" }),
            Log(logAttributes: new() { ["id"] = "4" }),
            Log(logAttributes: new() { ["id"] = "1" }),
        };

        var points = LogMetricAggregator.Aggregate(events, [Definition(groupBy: ["id"])], new LogMetricOptions { MaxGroupsPerDefinition = 2 }, Now);

        Assert.Equal(2, points.Single(p => p.DataPointAttributes.GetValueOrDefault("id") == "1").Value);
        Assert.Equal(1, points.Single(p => p.DataPointAttributes.GetValueOrDefault("id") == "2").Value);
        Assert.Equal(2, points.Single(p => p.DataPointAttributes.GetValueOrDefault("id") == LogMetricAggregator.OverflowValue).Value);
        Assert.DoesNotContain(points, p => p.DataPointAttributes.GetValueOrDefault("id") is "3" or "4");
    }

    [Fact]
    public void Aggregate_EmitsEveryDefinitionIndependently_AndNothingWithoutDefinitionsOrMatches()
    {
        var events = new[] { Log(severity: 17) };
        var errors = Definition(condition: new PipelineRuleCondition { SeverityNumbers = [17] });
        var all = Definition(metricName: "logs.all");

        Assert.Equal(2, LogMetricAggregator.Aggregate(events, [errors, all], new LogMetricOptions(), Now).Count);
        Assert.Empty(LogMetricAggregator.Aggregate(events, [], new LogMetricOptions(), Now));
        Assert.Empty(LogMetricAggregator.Aggregate([], [all], new LogMetricOptions(), Now));
        Assert.Empty(LogMetricAggregator.Aggregate(events, [Definition(condition: new PipelineRuleCondition { SeverityNumbers = [1] })], new LogMetricOptions(), Now));
    }

    private static LogMetricDefinition Definition(
        string metricName = "logs.errors",
        PipelineRuleCondition? condition = null,
        string[]? groupBy = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = metricName,
        MetricName = metricName,
        Condition = condition ?? new PipelineRuleCondition(),
        GroupBy = groupBy ?? [],
    };

    private static LogEvent Log(
        int severity = 9,
        string service = "svc",
        DateTimeOffset? at = null,
        Dictionary<string, string>? logAttributes = null,
        Dictionary<string, string>? resourceAttributes = null) => new()
    {
        EventId = Guid.NewGuid(),
        Timestamp = at ?? T0,
        IngestedAt = T0,
        SeverityNumber = severity,
        ServiceName = service,
        ResourceAttributes = resourceAttributes ?? new Dictionary<string, string>(),
        ScopeAttributes = new Dictionary<string, string>(),
        LogAttributes = logAttributes ?? new Dictionary<string, string>(),
    };
}
