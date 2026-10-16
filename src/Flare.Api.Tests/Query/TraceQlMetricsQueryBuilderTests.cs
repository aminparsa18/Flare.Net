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

    private static SpanMetricsSql BuildValue(SpanMetricFunction function, SpanValueAttribute value, double[]? quantiles = null, IReadOnlyList<SpanGroupBy>? groupBy = null) =>
        TraceQlMetricsQueryBuilder.Build(new SpanMetricsRequest(null, function, quantiles ?? [], groupBy ?? [], From, To, 60, value));

    [Fact]
    public void AttributeValue_IsCastToANumberAndSpansWithoutOneAreLeftOut()
    {
        var built = BuildValue(SpanMetricFunction.Avg, new SpanValueAttribute("span.size", SpanAttributeBag.Span, "size"));

        Assert.Contains("AND ifNull(isFinite(toFloat64OrNull(SpanAttributes[{mvKey0:String}])), 0)\nGROUP BY", built.Sql);
        Assert.Contains("sum(toFloat64(assumeNotNull(toFloat64OrNull(SpanAttributes[{mvKey0:String}]))) * SampleWeight) / sum(SampleWeight) AS V0", built.Sql);
        Assert.DoesNotContain("DurationNano", built.Sql);
        Assert.Equal("size", built.Parameters["mvKey0"].Value);
    }

    [Theory]
    [InlineData(SpanMetricFunction.Min, "toFloat64(min(assumeNotNull(toFloat64OrNull(ResourceAttributes[{mvKey0:String}])))) AS V0")]
    [InlineData(SpanMetricFunction.Max, "toFloat64(max(assumeNotNull(toFloat64OrNull(ResourceAttributes[{mvKey0:String}])))) AS V0")]
    [InlineData(SpanMetricFunction.Sum, "sum(toFloat64(assumeNotNull(toFloat64OrNull(ResourceAttributes[{mvKey0:String}]))) * SampleWeight) AS V0")]
    public void AttributeAggregates_ReadTheResourceBagAndWeighSumsOnly(SpanMetricFunction function, string expected) =>
        Assert.Contains(", " + expected + "\n", BuildValue(function, new SpanValueAttribute("resource.cpu", SpanAttributeBag.Resource, "cpu")).Sql);

    [Fact]
    public void AttributeQuantiles_UseTheWeightedDigestOverTheNumber()
    {
        var built = BuildValue(SpanMetricFunction.Quantile, new SpanValueAttribute("span.size", SpanAttributeBag.Span, "size"), [0.5, 0.99]);

        Assert.Contains(
            "quantileTDigestWeighted(0.5)(assumeNotNull(toFloat64OrNull(SpanAttributes[{mvKey0:String}])), SampleWeight) AS V0, " +
            "quantileTDigestWeighted(0.99)(assumeNotNull(toFloat64OrNull(SpanAttributes[{mvKey0:String}])), SampleWeight) AS V1",
            built.Sql);
    }

    [Fact]
    public void AttributeValue_ReadsWhicheverSemconvSpellingTheSpanHas()
    {
        var built = BuildValue(SpanMetricFunction.Sum, new SpanValueAttribute("span.http.status_code", SpanAttributeBag.Span, "http.status_code"));

        Assert.Contains(
            "multiIf(mapContains(SpanAttributes, {mvKey0:String}), toFloat64OrNull(SpanAttributes[{mvKey0:String}]), " +
            "mapContains(SpanAttributes, {mvKey1:String}), toFloat64OrNull(SpanAttributes[{mvKey1:String}]), NULL)",
            built.Sql);
        Assert.Equal("http.response.status_code", built.Parameters["mvKey1"].Value);
    }

    [Fact]
    public void AttributeValue_AlsoNarrowsTheRankingOfTheTopSeries()
    {
        var built = BuildValue(SpanMetricFunction.Avg, new SpanValueAttribute("span.size", SpanAttributeBag.Span, "size"), groupBy: [new SpanGroupBy("name", SpanGroupKind.Name)]);

        Assert.Equal(2, built.Sql.Split("ifNull(isFinite(toFloat64OrNull(SpanAttributes[{mvKey0:String}])), 0)").Length - 1);
    }

    [Fact]
    public void Histogram_GroupsByThePowerOfTwoBucketOfTheDuration()
    {
        var built = Build(SpanMetricFunction.Histogram);

        Assert.Contains("AND DurationNano >= 2 AND DurationNano < 9.2e18\n", built.Sql.Replace("GROUP BY", "\nGROUP BY"));
        Assert.Contains("toFloat64(sum(SampleWeight)) AS Weight, toUInt8(ceil(log2(toFloat64(toUInt64(DurationNano))))) AS V0", built.Sql);
        Assert.EndsWith("GROUP BY Bucket, V0\nORDER BY Bucket, V0", built.Sql);
    }

    [Fact]
    public void Histogram_RanksOnlyTheGroupsNotTheBuckets_AndKeepsFewerGroups()
    {
        var built = Build(SpanMetricFunction.Histogram, groupBy: [new SpanGroupBy("name", SpanGroupKind.Name)]);

        Assert.Contains($"LIMIT {TraceQlMetricsQueryBuilder.MaxHistogramGroups + 1})", built.Sql);
        Assert.EndsWith("GROUP BY Bucket, G0, V0\nORDER BY Bucket, G0, V0", built.Sql);
    }

    [Fact]
    public void Histogram_OfAnAttribute_BucketsTheNumber()
    {
        var built = BuildValue(SpanMetricFunction.Histogram, new SpanValueAttribute("span.size", SpanAttributeBag.Span, "size"));

        Assert.Contains("toUInt8(ceil(log2(toFloat64(toUInt64(assumeNotNull(toFloat64OrNull(SpanAttributes[{mvKey0:String}]))))))) AS V0", built.Sql);
        Assert.Contains("assumeNotNull(toFloat64OrNull(SpanAttributes[{mvKey0:String}])) >= 2", built.Sql);
    }

    [Fact]
    public void Exemplars_AreTheSlowestSpanPerStepAndGroup()
    {
        var request = new SpanMetricsRequest(null, SpanMetricFunction.Count, [], [new SpanGroupBy("name", SpanGroupKind.Name)], From, To, 60, Exemplars: 50);

        var built = TraceQlMetricsQueryBuilder.BuildExemplars(request);

        Assert.Equal(
            "SELECT intDiv(toInt64(toUnixTimestamp(StartTime)) - {mStart:Int64}, {mStep:Int64}) AS Bucket, Name AS G0, " +
            "argMax(TraceId, DurationNano) AS ExTrace, argMax(SpanId, DurationNano) AS ExSpan, toFloat64(max(DurationNano)) AS ExValue, " +
            "toInt64(toUnixTimestamp64Milli(argMax(StartTime, DurationNano))) AS ExTime\n" +
            "FROM spans WHERE StartTime >= {mFrom:DateTime64(9)} AND StartTime < {mTo:DateTime64(9)} AND 1 AND " +
            $"Name GLOBAL IN (SELECT Name FROM spans WHERE StartTime >= {{mFrom:DateTime64(9)}} AND StartTime < {{mTo:DateTime64(9)}} AND 1 GROUP BY Name ORDER BY sum(SampleWeight) DESC, Name LIMIT {TraceQlMetricsQueryBuilder.MaxSeries + 1})\n" +
            "GROUP BY Bucket, G0\nORDER BY ExValue DESC, Bucket, G0, ExTrace\nLIMIT 50",
            built.Sql);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(7, 7)]
    [InlineData(1_000_000, TraceQlMetricsQueryBuilder.MaxExemplars)]
    public void Exemplars_AreCapped(int asked, int limit)
    {
        var request = new SpanMetricsRequest(null, SpanMetricFunction.Avg, [], [], From, To, 60, Exemplars: asked);

        Assert.EndsWith($"LIMIT {limit}", TraceQlMetricsQueryBuilder.BuildExemplars(request).Sql);
    }

    [Fact]
    public void Exemplars_OfAnAttribute_PickTheLargestNumber()
    {
        var request = new SpanMetricsRequest(null, SpanMetricFunction.Max, [], [], From, To, 60, new SpanValueAttribute("span.size", SpanAttributeBag.Span, "size"), 10);

        Assert.Contains("argMax(TraceId, assumeNotNull(toFloat64OrNull(SpanAttributes[{mvKey0:String}]))) AS ExTrace", TraceQlMetricsQueryBuilder.BuildExemplars(request).Sql);
    }

    private static SpanMetricsSql BuildRelated(SpanAncestor? ancestor = null, SpanTraceRoot? root = null, TraceSpanCondition? span = null, IReadOnlyList<SpanGroupBy>? groupBy = null) =>
        TraceQlMetricsQueryBuilder.Build(new SpanMetricsRequest(
            span ?? new TraceSpanCondition { Name = "B", SpanName = "db.query" }, SpanMetricFunction.Count, [], groupBy ?? [], From, To, 60, Ancestor: ancestor, Root: root));

    [Fact]
    public void Root_RestrictsToTheTracesWhoseRootRowMatches()
    {
        var built = BuildRelated(root: new SpanTraceRoot(["web", "api"], ["GET /checkout"], 5, 50));

        Assert.Contains(
            "TraceId GLOBAL IN (SELECT TraceId FROM spans WHERE StartTime >= {mLookback:DateTime64(9)} AND StartTime < {mTo:DateTime64(9)} AND ParentSpanId = '' " +
            "AND ServiceName IN {mrServices:Array(String)} AND Name IN {mrNames:Array(String)} AND DurationNano >= {mrMin:UInt64} AND DurationNano <= {mrMax:UInt64})",
            built.Sql);
        Assert.Equal(From.UtcDateTime - TraceQlMetricsQueryBuilder.Lookback, built.Parameters["mLookback"].Value);
        Assert.Equal(new[] { "web", "api" }, (string[])built.Parameters["mrServices"].Value!);
        Assert.Equal(5UL, built.Parameters["mrMin"].Value);
    }

    [Fact]
    public void Root_OnlyNamesWhatTheQueryNamed()
    {
        var sql = BuildRelated(root: new SpanTraceRoot([], ["x"], null, null)).Sql;

        Assert.Contains("ParentSpanId = '' AND Name IN {mrNames:Array(String)})", sql);
        Assert.DoesNotContain("mrServices", sql);
        Assert.DoesNotContain("mrMin", sql);
    }

    [Fact]
    public void NoRelation_AddsNothing()
    {
        var sql = BuildRelated().Sql;

        Assert.DoesNotContain("mLookback", sql);
        Assert.DoesNotContain("ParentSpanId", sql);
    }

    [Fact]
    public void DirectParent_IsASemiJoinOnTheParentId()
    {
        var built = BuildRelated(new SpanAncestor(new TraceSpanCondition { Name = "A", SpanName = "svc.call" }, Direct: true));

        Assert.Contains(
            "(TraceId, ParentSpanId) GLOBAL IN (SELECT TraceId, SpanId FROM spans WHERE StartTime >= {mLookback:DateTime64(9)} AND StartTime < {mTo:DateTime64(9)} AND (Name = {tsPName:String}))",
            built.Sql);
        Assert.Equal("svc.call", built.Parameters["tsPName"].Value);
        Assert.Equal("db.query", built.Parameters["tsAName"].Value);
    }

    [Fact]
    public void AnyAncestor_WalksTheParentChainOfTheCandidateTraces()
    {
        var built = BuildRelated(new SpanAncestor(new TraceSpanCondition { Name = "A", SpanName = "svc.call" }, Direct: false));

        Assert.Contains("(TraceId, SpanId) GLOBAL IN (SELECT TraceId, SpanId FROM (SELECT TraceId, groupArray(cityHash64(SpanId)) AS allS", built.Sql);
        Assert.Contains("groupArrayIf(cityHash64(SpanId), (Name = {tsPName:String})) AS aboveS, groupArrayIf(SpanId, (Name = {tsAName:String})) AS belowIds", built.Sql);
        Assert.Contains("HAVING countIf((Name = {tsPName:String})) > 0 AND countIf((Name = {tsAName:String})) > 0", built.Sql);
        Assert.Contains("ARRAY JOIN belowIds AS SpanId, belowP AS startP", built.Sql);
        Assert.Contains($"range(least({TraceStructureSqlBuilder.MaxWalkDepth}, length(allS)))", built.Sql);
    }

    [Fact]
    public void ARelation_AlsoNarrowsTheRankingOfTheTopGroups()
    {
        var built = BuildRelated(new SpanAncestor(new TraceSpanCondition { Name = "A", SpanName = "svc.call" }, Direct: true), groupBy: [new SpanGroupBy("name", SpanGroupKind.Name)]);

        Assert.Equal(2, built.Sql.Split("(TraceId, ParentSpanId) GLOBAL IN").Length - 1);
    }

    [Fact]
    public void ARelation_NeedsACountedSpanCondition()
    {
        var request = new SpanMetricsRequest(null, SpanMetricFunction.Count, [], [], From, To, 60, Ancestor: new SpanAncestor(new TraceSpanCondition { Name = "A", SpanName = "x" }, true));

        Assert.Throws<ArgumentException>(() => TraceQlMetricsQueryBuilder.Build(request));
    }

    [Fact]
    public void CountIgnoresTheValue()
    {
        var built = BuildValue(SpanMetricFunction.Count, new SpanValueAttribute("span.size", SpanAttributeBag.Span, "size"));

        Assert.DoesNotContain("toFloat64OrNull", built.Sql);
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

    private static SpanMetricsSql BuildCompare(int topN = 10, DateTimeOffset? selectionFrom = null, DateTimeOffset? selectionTo = null, TraceSpanCondition? condition = null) =>
        TraceQlMetricsQueryBuilder.BuildCompare(new SpanMetricsRequest(
            condition, SpanMetricFunction.Compare, [], [], From, To, 600,
            Compare: new SpanCompare(new TraceSpanCondition { Name = "A", StatusCode = "STATUS_CODE_ERROR" }, topN, selectionFrom, selectionTo)));

    [Fact]
    public void Compare_RanksTheValuesOfEachAttributeInTwoPasses()
    {
        var built = BuildCompare(topN: 3);

        // Pass one: the topN + 1 most common values per group and attribute; pass two counts every value, the others as empty.
        Assert.Contains("ORDER BY sum(W) DESC, V LIMIT {mTopN:Int64} BY IsSel, A", built.Sql);
        Assert.Equal(4L, built.Parameters["mTopN"].Value);
        Assert.Contains("if((IsSel, A, V) GLOBAL IN (SELECT IsSel, A, V FROM", built.Sql);
        Assert.EndsWith("GROUP BY Bucket, IsSel, A, Kept\nORDER BY Bucket, IsSel, A, Kept", built.Sql);
        Assert.Contains("toFloat64(sum(W)) AS Weight", built.Sql);
    }

    [Fact]
    public void Compare_ReadsTheIntrinsicsAndBothAttributeMaps()
    {
        var sql = BuildCompare().Sql;

        Assert.Contains("('name', Name)", sql);
        Assert.Contains("multiIf(StatusCode = 'STATUS_CODE_ERROR', 'error', StatusCode = 'STATUS_CODE_OK', 'ok', 'unset')", sql);
        Assert.Contains("['unspecified', 'internal', 'server', 'client', 'producer', 'consumer'][least(toUInt16(Kind), 5) + 1]", sql);
        Assert.Contains("concat('span.', k), v), mapKeys(SpanAttributes), mapValues(SpanAttributes)", sql);
        Assert.Contains("concat('resource.', k), v), mapKeys(ResourceAttributes), mapValues(ResourceAttributes)", sql);
        Assert.Contains($"x.2 != '' AND length(x.2) <= {TraceQlMetricsQueryBuilder.MaxCompareValueLength}", sql);
    }

    [Fact]
    public void Compare_SplitsTheSpansOnTheSelectionsPredicate()
    {
        var built = BuildCompare(condition: new TraceSpanCondition { Name = "A", RootSpan = true });

        Assert.Contains("(StatusCode = {tsSStatus:String})) AS IsSel", built.Sql);
        Assert.Equal("STATUS_CODE_ERROR", built.Parameters["tsSStatus"].Value);
        Assert.Contains("(ParentSpanId = '')", built.Sql);
    }

    [Fact]
    public void Compare_WithAWindow_OnlySelectsSpansThatStartedInIt()
    {
        var built = BuildCompare(selectionFrom: From.AddMinutes(1), selectionTo: From.AddMinutes(2));

        Assert.Contains("AND StartTime > {mSelFrom:DateTime64(9)} AND StartTime <= {mSelTo:DateTime64(9)}) AS IsSel", built.Sql);
        Assert.Equal(From.AddMinutes(1).UtcDateTime, built.Parameters["mSelFrom"].Value);
    }

    [Fact]
    public void Compare_WithoutAWindow_HasNoSelectionWindow() => Assert.DoesNotContain("mSelFrom", BuildCompare().Sql);

    [Fact]
    public void Compare_LimitsTheNumberOfSteps()
    {
        var request = new SpanMetricsRequest(null, SpanMetricFunction.Compare, [], [], From, From.AddSeconds(60 * (TraceQlMetricsQueryBuilder.MaxCompareSteps + 1)), 60,
            Compare: new SpanCompare(new TraceSpanCondition { Name = "A", StatusCode = "STATUS_CODE_ERROR" }, 10, null, null));

        Assert.Throws<ArgumentException>(() => TraceQlMetricsQueryBuilder.BuildCompare(request));
    }

    [Fact]
    public void Compare_NeedsItsArguments() =>
        Assert.Throws<ArgumentException>(() => TraceQlMetricsQueryBuilder.BuildCompare(new SpanMetricsRequest(null, SpanMetricFunction.Compare, [], [], From, To, 60)));
}
