using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class TraceQlMetricsQueryBuilderTests
{
    private static readonly DateTimeOffset From = DateTimeOffset.FromUnixTimeSeconds(1_790_000_040);
    private static readonly DateTimeOffset To = From.AddMinutes(10);

    private static SpanMetricsSql Build(
        SpanMetricFunction function = SpanMetricFunction.Count,
        TraceSpanCondition? condition = null,
        IReadOnlyList<SpanGroupBy>? groupBy = null,
        double[]? quantiles = null) =>
        TraceQlMetricsQueryBuilder.Build(new SpanMetricsRequest(condition, function, quantiles ?? [], groupBy ?? [], From, To, 60));

    [Fact]
    public void Count_GroupsSpansIntoStepsAndWeighsThem()
    {
        var built = Build(condition: new TraceSpanCondition { Name = "A", ServiceName = "api", StatusCode = "STATUS_CODE_ERROR" });

        Assert.Equal(
            "SELECT intDiv(toInt64(toUnixTimestamp(StartTime)) - {mStart:Int64}, {mStep:Int64}) AS Bucket, toFloat64(sum(SampleWeight)) AS Weight\n" +
            "FROM spans WHERE StartTime >= {mFrom:DateTime64(9)} AND StartTime < {mTo:DateTime64(9)} AND " +
            "(ServiceName = {tsAService:String} AND StatusCode = {tsAStatus:String})\n" +
            "GROUP BY Bucket\n" +
            "ORDER BY Bucket",
            built.Sql);
        Assert.Equal(1_790_000_040L, built.Parameters["mStart"].Value);
        Assert.Equal(60L, built.Parameters["mStep"].Value);
        Assert.Equal(From.UtcDateTime, built.Parameters["mFrom"].Value);
        Assert.Equal(To.UtcDateTime, built.Parameters["mTo"].Value);
    }

    [Fact]
    public void NoCondition_MatchesEverySpanInTheWindow()
    {
        var built = Build();

        Assert.Contains("WHERE StartTime >= {mFrom:DateTime64(9)} AND StartTime < {mTo:DateTime64(9)}\nGROUP BY", built.Sql);
    }

    [Theory]
    [InlineData(SpanMetricFunction.Avg, "sum(toFloat64(DurationNano) * SampleWeight) / sum(SampleWeight) AS V0")]
    [InlineData(SpanMetricFunction.Min, "toFloat64(min(DurationNano)) AS V0")]
    [InlineData(SpanMetricFunction.Max, "toFloat64(max(DurationNano)) AS V0")]
    [InlineData(SpanMetricFunction.Sum, "sum(toFloat64(DurationNano) * SampleWeight) AS V0")]
    public void DurationAggregates_AreWeighted(SpanMetricFunction function, string expected) =>
        Assert.Contains(", " + expected + "\n", Build(function).Sql);

    [Fact]
    public void Quantiles_UseTheWeightedDigestOneColumnEach()
    {
        var built = Build(SpanMetricFunction.Quantile, quantiles: [0.5, 0.95, 1]);

        Assert.Contains(
            "quantileTDigestWeighted(0.5)(DurationNano, SampleWeight) AS V0, " +
            "quantileTDigestWeighted(0.95)(DurationNano, SampleWeight) AS V1, " +
            "quantileTDigestWeighted(1)(DurationNano, SampleWeight) AS V2",
            built.Sql);
    }

    [Fact]
    public void Grouping_RanksGroupsInASubqueryAndKeepsOneMoreThanTheCap()
    {
        var built = Build(groupBy: [new SpanGroupBy("name", SpanGroupKind.Name), new SpanGroupBy("status", SpanGroupKind.Status), new SpanGroupBy("kind", SpanGroupKind.Kind)]);

        Assert.Contains("Name AS G0, toString(StatusCode) AS G1, toString(Kind) AS G2", built.Sql);
        Assert.Contains("(Name, toString(StatusCode), toString(Kind)) GLOBAL IN (SELECT Name, toString(StatusCode), toString(Kind) FROM spans WHERE ", built.Sql);
        Assert.Contains($"ORDER BY sum(SampleWeight) DESC, Name, toString(StatusCode), toString(Kind) LIMIT {TraceQlMetricsQueryBuilder.MaxSeries + 1})", built.Sql);
        Assert.EndsWith("GROUP BY Bucket, G0, G1, G2\nORDER BY Bucket, G0, G1, G2", built.Sql);
    }

    [Fact]
    public void SingleGroup_NeedsNoTuple()
    {
        var built = Build(groupBy: [new SpanGroupBy("name", SpanGroupKind.Name)]);

        Assert.Contains("AND Name GLOBAL IN (SELECT Name FROM spans", built.Sql);
    }

    [Fact]
    public void AttributeGroup_ReadsTheBagAndDropsSpansWithoutIt()
    {
        var built = Build(groupBy: [new SpanGroupBy("resource.k8s.pod.name", SpanGroupKind.Attribute, SpanAttributeBag.Resource, "k8s.pod.name")]);

        Assert.Contains("ResourceAttributes[{mgKey0x0:String}] AS G0", built.Sql);
        Assert.Contains("AND mapContains(ResourceAttributes, {mgKey0x0:String}) AND", built.Sql);
        Assert.Equal("k8s.pod.name", built.Parameters["mgKey0x0"].Value);
    }

    [Fact]
    public void AttributeGroup_ReadsWhicheverSemconvSpellingTheSpanHas()
    {
        var built = Build(groupBy: [new SpanGroupBy("span.http.status_code", SpanGroupKind.Attribute, SpanAttributeBag.Span, "http.status_code")]);

        Assert.Contains(
            "multiIf(mapContains(SpanAttributes, {mgKey0x0:String}), SpanAttributes[{mgKey0x0:String}], " +
            "mapContains(SpanAttributes, {mgKey0x1:String}), SpanAttributes[{mgKey0x1:String}], '') AS G0",
            built.Sql);
        Assert.Contains("(mapContains(SpanAttributes, {mgKey0x0:String}) OR mapContains(SpanAttributes, {mgKey0x1:String}))", built.Sql);
        Assert.Equal("http.status_code", built.Parameters["mgKey0x0"].Value);
        Assert.Equal("http.response.status_code", built.Parameters["mgKey0x1"].Value);
    }

    [Fact]
    public void AppliesTheServiceScope()
    {
        ServiceScope.Current = ["checkout"];
        try
        {
            Assert.Contains("ServiceName IN {scopeExact:Array(String)}", Build().Sql);
        }
        finally
        {
            ServiceScope.Current = null;
        }
    }

    [Fact]
    public void RejectsWhatCannotBeBuilt()
    {
        var request = new SpanMetricsRequest(null, SpanMetricFunction.Count, [], [], From, To, 60);

        Assert.Throws<ArgumentException>(() => TraceQlMetricsQueryBuilder.Build(request with { StepSeconds = 0 }));
        Assert.Throws<ArgumentException>(() => TraceQlMetricsQueryBuilder.Build(request with { To = From }));
        Assert.Throws<ArgumentException>(() => TraceQlMetricsQueryBuilder.Build(request with { StepSeconds = 1, To = From.AddSeconds(TraceQlMetricsQueryBuilder.MaxSteps + 1) }));
        Assert.Throws<ArgumentException>(() => TraceQlMetricsQueryBuilder.Build(request with { Function = SpanMetricFunction.Quantile, Quantiles = [1.5] }));
        Assert.Throws<ArgumentException>(() => TraceQlMetricsQueryBuilder.Build(request with { Function = SpanMetricFunction.Quantile, Quantiles = [double.NaN] }));
    }
}
