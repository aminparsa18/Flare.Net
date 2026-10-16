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
        Assert.Equal(new TraceQlRank(true, 3), Assert.Single(plan.Stages));
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

    [Fact]
    public void RootFields_RestrictTheTracesNotTheSpans()
    {
        var plan = Plan("""{ rootName = "GET /checkout" && rootServiceName = "web" && traceDuration > 1s && status = error } | rate()""");

        var root = Assert.IsType<SpanTraceRoot>(plan.Root);
        Assert.Equal(new[] { "web" }, root.Services);
        Assert.Equal(new[] { "GET /checkout" }, root.Names);
        Assert.Equal(1_000_000_001UL, root.MinDurationNano);
        Assert.Null(root.MaxDurationNano);
        Assert.Equal("STATUS_CODE_ERROR", plan.Condition!.StatusCode);
        Assert.Null(plan.Ancestor);
    }

    [Fact]
    public void OnlyRootFields_CountEverySpanOfTheMatchingTraces()
    {
        var plan = Plan("""{ traceDuration < 2s } | count_over_time()""");

        Assert.Null(plan.Condition);
        var root = Assert.IsType<SpanTraceRoot>(plan.Root);
        Assert.Empty(root.Services);
        Assert.Empty(root.Names);
        Assert.Equal(1_999_999_999UL, root.MaxDurationNano);
    }

    [Fact]
    public void NoRootFields_NoRoot() => Assert.Null(Plan("{ } | rate()").Root);

    [Theory]
    [InlineData(">>", false)]
    [InlineData(">", true)]
    public void AStructuralQuery_CountsTheRightSpansetWithTheLeftAsItsAncestor(string op, bool direct)
    {
        var plan = Plan($$"""{ resource.service.name = "web" } {{op}} { name = "db.query" && status = error } | rate()""");

        Assert.Equal("db.query", plan.Condition!.SpanName);
        Assert.Equal("STATUS_CODE_ERROR", plan.Condition.StatusCode);
        Assert.Equal("web", plan.Ancestor!.Condition.ServiceName);
        Assert.Equal(direct, plan.Ancestor.Direct);
    }

    [Fact]
    public void AnEmptySideOfARelation_IsAnySpan()
    {
        var plan = Plan("{ name = \"a\" } >> { } | rate()");

        Assert.Equal(0UL, plan.Condition!.MinDurationNano);
        Assert.Equal("a", plan.Ancestor!.Condition.SpanName);
    }

    [Fact]
    public void RootFieldsNextToARelation_AreRejectedAsInSearch() =>
        Assert.Throws<TraceQlException>(() => Plan("""{ rootName = "x" } >> { name = "b" } | rate()"""));

    [Fact]
    public void ASpanPredicateErrorFromTheSearchPlannerSurfaces()
    {
        Assert.Throws<TraceQlException>(() => Plan("{ statusMessage = \"x\" } | rate()"));
    }

    [Fact]
    public void Compare_PlansTheSelectionAsASpanCondition()
    {
        var plan = Plan("{nestedSetParent<0 && true} | compare({status = error && span.http.route = \"/x\"}, 5)");

        Assert.Equal(SpanMetricFunction.Compare, plan.Function);
        Assert.True(plan.Condition!.RootSpan);
        var compare = Assert.IsType<SpanCompare>(plan.Compare);
        Assert.Equal("STATUS_CODE_ERROR", compare.Selection.StatusCode);
        Assert.Equal(5, compare.TopN);
        Assert.Null(compare.From);
    }

    [Fact]
    public void Compare_TurnsTheWindowFromNanosecondsIntoTimes()
    {
        var compare = Plan("{ } | compare({ status = error }, 10, 1700000000000000000, 1700000003600000000)").Compare!;

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), compare.From);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_003) + TimeSpan.FromMilliseconds(600), compare.To);
    }

    [Theory]
    [InlineData("{ } | compare({ rootName = \"x\" })")]
    [InlineData("{ } | compare({ traceDuration > 1s })")]
    public void Compare_NeedsASpanSelection(string traceQl) => Assert.Throws<TraceQlException>(() => Plan(traceQl));

    [Fact]
    public void Compare_OfAnEmptySelection_SelectsEverySpan() =>
        Assert.Equal(0UL, Plan("{ } | compare({ })").Compare!.Selection.MinDurationNano);

    [Fact]
    public void ANonCompareQuery_HasNoCompare() => Assert.Null(Plan("{ } | rate()").Compare);

    [Theory]
    [InlineData("~", TraceRelationKind.Sibling, false)]
    [InlineData("!>", TraceRelationKind.Child, true)]
    [InlineData("!>>", TraceRelationKind.Descendant, true)]
    [InlineData("!~", TraceRelationKind.Sibling, true)]
    public void TheOtherStructuralOperators_CountTheRightSpansetWithTheLeftAsTheRelatedSpan(string op, TraceRelationKind kind, bool negated)
    {
        var plan = Plan($$"""{ resource.service.name = "web" } {{op}} { name = "db.query" } | rate()""");

        Assert.Equal("db.query", plan.Condition!.SpanName);
        Assert.Equal("web", plan.Ancestor!.Condition.ServiceName);
        Assert.Equal(kind, plan.Ancestor.Kind);
        Assert.Equal(negated, plan.Ancestor.Negated);
    }

    [Theory]
    [InlineData("&>>")]
    [InlineData("&>")]
    [InlineData("&~")]
    public void UnionOperators_AreRejectedInAMetricsQuery_NamingTheOperator(string op) =>
        Assert.Contains("union operator", Assert.Throws<TraceQlException>(() => Plan($$"""{ name = "a" } {{op}} { name = "b" } | rate()""")).Message);

    [Fact]
    public void AnEmptySideOfAnyRelation_IsAnySpan()
    {
        var plan = Plan("{ name = \"a\" } !~ { } | rate()");

        Assert.Equal(0UL, plan.Condition!.MinDurationNano);
        Assert.True(plan.Ancestor!.Negated);
    }
}
