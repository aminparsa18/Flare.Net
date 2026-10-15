using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Api.Tempo;
using Xunit;

namespace Flare.Api.Tests.Tempo;

public class TempoMetricsPlannerTests
{
    private static TempoMetricsPlan Plan(string traceQl) => TempoMetricsPlanner.Plan(TraceQlParser.ParseMetrics(traceQl));

    [Fact]
    public void TheSpansetBecomesOneSpanCondition()
    {
        var plan = Plan("""{ resource.service.name = "api" && status = error && span.http.route =~ "/c.*" } | rate()""");

        var condition = Assert.IsType<TraceSpanCondition>(plan.Condition);
        Assert.Equal("api", condition.ServiceName);
        Assert.Equal("STATUS_CODE_ERROR", condition.StatusCode);
        Assert.Equal("^(?:/c.*)$", Assert.Single(condition.Attributes!).Value);
    }

    [Theory]
    [InlineData("span.size", SpanAttributeBag.Span, "size")]
    [InlineData("resource.k8s.pod.cpu", SpanAttributeBag.Resource, "k8s.pod.cpu")]
    [InlineData(".k8s.pod.cpu", SpanAttributeBag.Resource, "k8s.pod.cpu")]
    [InlineData(".http.response.body.size", SpanAttributeBag.Span, "http.response.body.size")]
    public void ANumericAttribute_IsTheValueToAggregate(string field, SpanAttributeBag bag, string key)
    {
        var plan = Plan($"{{ }} | quantile_over_time({field}, .9)");

        Assert.Equal(new SpanValueAttribute(field, bag, key), plan.Value);
    }

    [Fact]
    public void Histogram_RankAndExemplars_AreCarriedOver()
    {
        var plan = Plan("{ } | histogram_over_time(duration) by (name) | topk(3) with(exemplars=true)");

        Assert.Equal(SpanMetricFunction.Histogram, plan.Function);
        Assert.Equal(new TraceQlRank(true, 3), plan.Rank);
        Assert.True(plan.Exemplars);
    }

    [Fact]
    public void Duration_HasNoValueAttribute() => Assert.Null(Plan("{ } | avg_over_time(duration)").Value);

    [Fact]
    public void AnEmptySpanset_MatchesEverySpan()
    {
        Assert.Null(Plan("{ } | count_over_time()").Condition);
    }

    [Theory]
    [InlineData("rate", SpanMetricFunction.Count, true)]
    [InlineData("count_over_time", SpanMetricFunction.Count, false)]
    [InlineData("avg_over_time(duration)", SpanMetricFunction.Avg, false)]
    [InlineData("min_over_time(duration)", SpanMetricFunction.Min, false)]
    [InlineData("max_over_time(duration)", SpanMetricFunction.Max, false)]
    [InlineData("sum_over_time(duration)", SpanMetricFunction.Sum, false)]
    public void FunctionsMapToTheirAggregate(string function, SpanMetricFunction expected, bool isRate)
    {
        var plan = Plan("{ } | " + (function.Contains('(') ? function : function + "()"));

        Assert.Equal(expected, plan.Function);
        Assert.Equal(isRate, plan.IsRate);
    }

    [Fact]
    public void QuantilesAreKept()
    {
        var plan = Plan("{ } | quantile_over_time(duration, .5, .99)");

        Assert.Equal(SpanMetricFunction.Quantile, plan.Function);
        Assert.Equal(new[] { 0.5, 0.99 }, plan.Quantiles);
    }

    [Fact]
    public void GroupFields_ResolveTheirSourceAndKeepTheirLabel()
    {
        var plan = Plan("{ } | rate() by (name, status, kind, span.http.route, resource.k8s.pod.name)");

        Assert.Equal(
            new[]
            {
                new SpanGroupBy("name", SpanGroupKind.Name),
                new SpanGroupBy("status", SpanGroupKind.Status),
                new SpanGroupBy("kind", SpanGroupKind.Kind),
                new SpanGroupBy("span.http.route", SpanGroupKind.Attribute, SpanAttributeBag.Span, "http.route"),
                new SpanGroupBy("resource.k8s.pod.name", SpanGroupKind.Attribute, SpanAttributeBag.Resource, "k8s.pod.name"),
            },
            plan.GroupBy);
    }

    [Fact]
    public void UnscopedGroupFields_UseTheSameGuessAsAPredicate()
    {
        var plan = Plan("{ } | rate() by (.service.name, .http.method)");

        Assert.Equal(
            new[]
            {
                new SpanGroupBy(".service.name", SpanGroupKind.Attribute, SpanAttributeBag.Resource, "service.name"),
                new SpanGroupBy(".http.method", SpanGroupKind.Attribute, SpanAttributeBag.Span, "http.method"),
            },
            plan.GroupBy);
    }

    [Theory]
    [InlineData("{ rootName = \"x\" } | rate()")]
    [InlineData("{ rootServiceName = \"x\" } | rate()")]
    [InlineData("{ traceDuration > 1s } | rate()")]
    public void TraceLevelFields_AreRejected(string traceQl)
    {
        var ex = Assert.Throws<TraceQlException>(() => Plan(traceQl));

        Assert.Contains("whole trace", ex.Message);
    }

    [Fact]
    public void ASpanPredicateErrorFromTheSearchPlannerSurfaces()
    {
        Assert.Throws<TraceQlException>(() => Plan("{ statusMessage = \"x\" } | rate()"));
    }
}
