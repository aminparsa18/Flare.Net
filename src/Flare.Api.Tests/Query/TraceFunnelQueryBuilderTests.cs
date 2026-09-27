using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class TraceFunnelQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly TraceFunnelStep Checkout = new() { ServiceName = "frontend", SpanName = "POST /checkout" };
    private static readonly TraceFunnelStep Payment = new() { ServiceName = "payment", SpanName = "charge" };
    private static readonly TraceFunnelStep Email = new() { ServiceName = "email" };

    private static TraceFunnelRequest Request(params TraceFunnelStep[] steps) => new() { Steps = steps };

    [Theory]
    [InlineData(null, TraceFunnelQueryBuilder.DefaultWindowMinutes)]
    [InlineData(0, TraceFunnelQueryBuilder.DefaultWindowMinutes)]
    [InlineData(1, TraceFunnelQueryBuilder.MinWindowMinutes)]
    [InlineData(360, 360)]
    [InlineData(100_000, TraceFunnelQueryBuilder.MaxWindowMinutes)]
    public void ClampWindowMinutes_ClampsAndDefaults(int? requested, int expected)
    {
        Assert.Equal(expected, TraceFunnelQueryBuilder.ClampWindowMinutes(requested));
    }

    [Fact]
    public void ValidateSteps_RejectsTooFewTooManyAndEmptySteps()
    {
        Assert.Throws<ArgumentException>(() => TraceFunnelQueryBuilder.ValidateSteps(null));
        Assert.Throws<ArgumentException>(() => TraceFunnelQueryBuilder.ValidateSteps([Checkout]));
        Assert.Throws<ArgumentException>(() => TraceFunnelQueryBuilder.ValidateSteps(Enumerable.Repeat(Checkout, TraceFunnelQueryBuilder.MaxSteps + 1).ToList()));
        var ex = Assert.Throws<ArgumentException>(() => TraceFunnelQueryBuilder.ValidateSteps([Checkout, new TraceFunnelStep { Attributes = [] }]));
        Assert.Contains("Step 2", ex.Message);
    }

    [Fact]
    public void ValidateSteps_AcceptsAnAttributeOnlyStep()
    {
        var attributeOnly = new TraceFunnelStep { Attributes = [new SpanAttributeFilter { Key = "order.state", Value = "paid" }] };

        Assert.Equal(2, TraceFunnelQueryBuilder.ValidateSteps([Checkout, attributeOnly]).Count);
    }

    [Fact]
    public void BuildSummary_GroupsWindowedMatchingSpansByTrace_OneSortedArrayPerStep()
    {
        var result = TraceFunnelQueryBuilder.BuildSummary(Request(Checkout, Payment, Email), Now);

        Assert.Contains("FROM spans", result.Sql);
        Assert.Contains("StartTime >= {from:DateTime64(9)} AND StartTime < {to:DateTime64(9)}", result.Sql);
        Assert.Contains("GROUP BY TraceId", result.Sql);
        Assert.Contains("HAVING length(m1) > 0", result.Sql);
        Assert.Contains(
            "arraySort(groupArrayIf((StartTime, cityHash64(SpanId), StatusCode = 'STATUS_CODE_ERROR'), (ServiceName = {s1Service:String} AND Name = {s1Name:String}))) AS m1",
            result.Sql);
        Assert.Contains("(ServiceName = {s3Service:String}))) AS m3", result.Sql);
        Assert.Contains(
            "((ServiceName = {s1Service:String} AND Name = {s1Name:String}) OR (ServiceName = {s2Service:String} AND Name = {s2Name:String}) OR (ServiceName = {s3Service:String}))",
            result.Sql);

        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(Now.AddMinutes(-60).UtcDateTime, parameters["from"]);
        Assert.Equal(Now.UtcDateTime, parameters["to"]);
        Assert.Equal("frontend", parameters["s1Service"]);
        Assert.Equal("POST /checkout", parameters["s1Name"]);
        Assert.Equal("email", parameters["s3Service"]);
        Assert.False(parameters.ContainsKey("s3Name"));
    }

    [Fact]
    public void BuildSummary_WalksStepsInOrder_EachAtOrAfterThePreviousAndNotTheSameSpan()
    {
        var result = TraceFunnelQueryBuilder.BuildSummary(Request(Checkout, Payment, Email), Now);

        Assert.Contains("1 AS i1", result.Sql);
        Assert.Contains("if(i1 = 0, 0, arrayFirstIndex(x -> x.1 >= m1[i1].1 AND x.2 != m1[i1].2, m2)) AS i2", result.Sql);
        Assert.Contains("if(i2 = 0, 0, arrayFirstIndex(x -> x.1 >= m2[i2].1 AND x.2 != m2[i2].2, m3)) AS i3", result.Sql);
    }

    [Fact]
    public void BuildSummary_FourColumnsPerStep_FirstStepHasNoTransition()
    {
        var result = TraceFunnelQueryBuilder.BuildSummary(Request(Checkout, Payment), Now);
        var select = result.Sql[..result.Sql.IndexOf("FROM", StringComparison.Ordinal)];

        Assert.Contains("countIf(i1 > 0),\n    countIf(i1 > 0 AND m1[i1].3),\n    toFloat64(0),\n    [toFloat64(0), 0, 0]", select);
        Assert.Contains("countIf(i2 > 0),\n    countIf(i2 > 0 AND m2[i2].3)", select);
        Assert.Contains("avgIf((toUnixTimestamp64Nano(m2[i2].1) - toUnixTimestamp64Nano(m1[i1].1)) / 1e6, i2 > 0)", select);
        Assert.Contains("quantilesIf(0.5, 0.95, 0.99)((toUnixTimestamp64Nano(m2[i2].1) - toUnixTimestamp64Nano(m1[i1].1)) / 1e6, i2 > 0)", select);
        Assert.Equal(8, select.Split(",\n").Length);
    }

    [Fact]
    public void BuildSummary_PrefiltersServices_OnlyWhenEveryStepNamesOne()
    {
        var allNamed = TraceFunnelQueryBuilder.BuildSummary(Request(Checkout, Payment, Checkout), Now);
        Assert.Contains("ServiceName IN {funnelServices:Array(String)}", allNamed.Sql);
        Assert.Equal(new[] { "frontend", "payment" }, allNamed.Parameters.ToDictionary()["funnelServices"]);

        var nameOnly = new TraceFunnelStep { SpanName = "confirm" };
        var mixed = TraceFunnelQueryBuilder.BuildSummary(Request(Checkout, nameOnly), Now);
        Assert.DoesNotContain("funnelServices", mixed.Sql);
    }

    [Fact]
    public void BuildSummary_AttributeFilters_BindPerStepParameterNames()
    {
        var paid = new TraceFunnelStep
        {
            SpanName = "charge",
            Attributes = [new SpanAttributeFilter { Key = "payment.method", Value = "card" }],
        };
        var regional = new TraceFunnelStep
        {
            Attributes = [new SpanAttributeFilter { Bag = SpanAttributeBag.Resource, Key = "cloud.region", Value = "eu-.*", Operator = SpanAttributeFilterOperator.Regex }],
        };

        var result = TraceFunnelQueryBuilder.BuildSummary(Request(Checkout, paid, regional), Now);

        Assert.Contains("(Name = {s2Name:String} AND SpanAttributes[{attrKeyS2_0:String}] = {attrValueS2_0:String})", result.Sql);
        Assert.Contains("(mapContains(ResourceAttributes, {attrKeyS3_0:String}) AND match(ResourceAttributes[{attrKeyS3_0:String}], {attrValueS3_0:String}))", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("payment.method", parameters["attrKeyS2_0"]);
        Assert.Equal("eu-.*", parameters["attrValueS3_0"]);
    }

    [Fact]
    public void BuildSummary_HonorsEndUnixMsAndWindow()
    {
        var end = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var request = Request(Checkout, Payment) with { WindowMinutes = 15, EndUnixMs = end.ToUnixTimeMilliseconds() };

        var parameters = TraceFunnelQueryBuilder.BuildSummary(request, Now).Parameters.ToDictionary();

        Assert.Equal(end.AddMinutes(-15).UtcDateTime, parameters["from"]);
        Assert.Equal(end.UtcDateTime, parameters["to"]);
    }

    [Theory]
    [InlineData(0, TraceFunnelOutcome.Dropped, "WHERE i1 > 0 AND i2 = 0")]
    [InlineData(1, TraceFunnelOutcome.Reached, "WHERE i2 > 0\n")]
    [InlineData(1, TraceFunnelOutcome.Errored, "WHERE i2 > 0 AND m2[i2].3")]
    public void BuildTraces_FiltersToTheRequestedOutcome(int stepIndex, TraceFunnelOutcome outcome, string expected)
    {
        var request = new TraceFunnelTracesRequest { Steps = [Checkout, Payment, Email], StepIndex = stepIndex, Outcome = outcome };

        var result = TraceFunnelQueryBuilder.BuildTraces(request, Now);

        Assert.Contains(expected, result.Sql);
        Assert.Contains("ORDER BY startMs DESC, TraceId", result.Sql);
        Assert.EndsWith($"LIMIT {TraceFunnelQueryBuilder.MaxTraces}", result.Sql);
    }

    [Fact]
    public void BuildTraces_ReportsReachedStepsAndElapsedToTheLastReachedStep()
    {
        var request = new TraceFunnelTracesRequest { Steps = [Checkout, Payment, Email], StepIndex = 0, Outcome = TraceFunnelOutcome.Reached };

        var result = TraceFunnelQueryBuilder.BuildTraces(request, Now);

        Assert.Contains("toInt32(1 + (i2 > 0) + (i3 > 0)) AS reachedSteps", result.Sql);
        Assert.Contains("toUnixTimestamp64Nano(multiIf(i3 > 0, m3[i3].1, i2 > 0, m2[i2].1, m1[1].1))", result.Sql);
    }

    [Fact]
    public void BuildTraces_RejectsOutOfRangeStepAndDropOffFromTheLastStep()
    {
        TraceFunnelStep[] steps = [Checkout, Payment];

        Assert.Throws<ArgumentException>(() => TraceFunnelQueryBuilder.BuildTraces(new TraceFunnelTracesRequest { Steps = steps, StepIndex = 2 }, Now));
        Assert.Throws<ArgumentException>(() => TraceFunnelQueryBuilder.BuildTraces(new TraceFunnelTracesRequest { Steps = steps, StepIndex = -1 }, Now));
        Assert.Throws<ArgumentException>(() => TraceFunnelQueryBuilder.BuildTraces(new TraceFunnelTracesRequest { Steps = steps, StepIndex = 1, Outcome = TraceFunnelOutcome.Dropped }, Now));
    }
}
