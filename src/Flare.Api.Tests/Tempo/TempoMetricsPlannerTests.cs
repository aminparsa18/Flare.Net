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
        Assert.Throws<TraceQlException>(() => Plan("{ childCount > 1 } | rate()"));
    }

    [Fact]
    public void Compare_PlansTheSelectionAsASpanCondition()
    {
        var plan = Plan("{nestedSetParent<0 && true} | compare({status = error && span.http.route = \"/x\"}, 5)");

        Assert.Equal(SpanMetricFunction.Compare, plan.Function);
        Assert.True(plan.Condition!.RootSpan);
        var compare = Assert.IsType<SpanCompare>(plan.Compare);
        Assert.Equal("STATUS_CODE_ERROR", Assert.IsType<SpanSetCondition>(compare.Selection).Condition.StatusCode);
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
        Assert.Equal(0UL, Assert.IsType<SpanSetCondition>(Plan("{ } | compare({ })").Compare!.Selection).Condition.MinDurationNano);

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
    [InlineData("&>>", TraceRelationKind.Descendant)]
    [InlineData("&>", TraceRelationKind.Child)]
    [InlineData("&~", TraceRelationKind.Sibling)]
    public void UnionOperators_CountTheSpansOfBothSides(string op, TraceRelationKind kind)
    {
        var plan = Plan($$"""{ name = "a" } {{op}} { name = "b" } | rate()""");

        var relation = Assert.IsType<SpanSetRelation>(plan.Spansets);
        Assert.Equal("a", relation.Left.SpanName);
        Assert.Equal("b", relation.Right.SpanName);
        Assert.Equal(kind, relation.Kind);
        Assert.True(relation.Union);
        Assert.False(relation.Negated);
        Assert.Null(plan.Condition);
        Assert.Null(plan.Ancestor);
    }

    [Fact]
    public void AnEmptySideOfAnyRelation_IsAnySpan()
    {
        var plan = Plan("{ name = \"a\" } !~ { } | rate()");

        Assert.Equal(0UL, plan.Condition!.MinDurationNano);
        Assert.True(plan.Ancestor!.Negated);
    }

    [Fact]
    public void AndAndOr_BuildATreeOverTheSpansetsConditions()
    {
        var plan = Plan("""({ name = "a" } && { name = "b" }) || { status = error } | rate()""");

        var or = Assert.IsType<SpanSetOr>(plan.Spansets);
        var and = Assert.IsType<SpanSetAnd>(or.Left);
        Assert.Equal("a", Assert.IsType<SpanSetCondition>(and.Left).Condition.SpanName);
        Assert.Equal("b", Assert.IsType<SpanSetCondition>(and.Right).Condition.SpanName);
        Assert.Equal("STATUS_CODE_ERROR", Assert.IsType<SpanSetCondition>(or.Right).Condition.StatusCode);
        Assert.Equal(["A", "B", "C"], new[] { and.Left, and.Right, or.Right }.Select(n => ((SpanSetCondition)n).Condition.Name));
        Assert.Null(plan.Condition);
    }

    [Fact]
    public void ARelationInsideACombination_KeepsItsKindAndSides()
    {
        var plan = Plan("""{ name = "a" } !>> { name = "b" } && { name = "c" } | rate()""");

        var and = Assert.IsType<SpanSetAnd>(plan.Spansets);
        var relation = Assert.IsType<SpanSetRelation>(and.Left);
        Assert.Equal((TraceRelationKind.Descendant, true, false), (relation.Kind, relation.Negated, relation.Union));
    }

    [Fact]
    public void ACombination_GroupsAndAggregatesLikeASpanset()
    {
        var plan = Plan("""{ name = "a" } || { name = "b" } | quantile_over_time(duration, .5) by (resource.service.name)""");

        Assert.Equal(SpanMetricFunction.Quantile, plan.Function);
        Assert.Equal("resource.service.name", Assert.Single(plan.GroupBy).Label);
    }

    [Theory]
    [InlineData("""{ rootName = "x" } || { name = "b" } | rate()""", "single spanset")]
    [InlineData("""{ name = "a" } || { } | rate()""", "empty spanset")]
    [InlineData("""{ name = "a" } && { true } | rate()""", "empty spanset")]
    public void ACombination_RejectsWhatTheSearchPlannerRejects(string traceQl, string message) =>
        Assert.Contains(message, Assert.Throws<TraceQlException>(() => Plan(traceQl)).Message);

    [Fact]
    public void Compare_OfACombination_IsASpanSetExpressionOverItsSpansets()
    {
        var compare = Plan("{ } | compare({ status = error } || { name = \"x\" } && { kind = server })").Compare!;

        var or = Assert.IsType<SpanSetOr>(compare.Selection);
        Assert.Equal("STATUS_CODE_ERROR", Assert.IsType<SpanSetCondition>(or.Left).Condition.StatusCode);
        var and = Assert.IsType<SpanSetAnd>(or.Right);
        Assert.Equal("x", Assert.IsType<SpanSetCondition>(and.Left).Condition.SpanName);
        Assert.Equal((byte)2, Assert.IsType<SpanSetCondition>(and.Right).Condition.Kind);
    }

    [Fact]
    public void Compare_OfACombination_KeepsTheWindowAndTheNumberOfValues()
    {
        var compare = Plan("{ } | compare({ status = error } || { name = \"x\" }, 7, 1700000000000000000, 1700000003600000000)").Compare!;

        Assert.Equal(7, compare.TopN);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), compare.From);
    }

    [Fact]
    public void Compare_OfARelation_IsASpanSetRelation()
    {
        var compare = Plan("{ } | compare({ name = \"a\" } !> { name = \"b\" })").Compare!;

        var relation = Assert.IsType<SpanSetRelation>(compare.Selection);
        Assert.Equal((TraceRelationKind.Child, true), (relation.Kind, relation.Negated));
    }

    [Fact]
    public void Compare_OfACombination_AlsoWorksBesideACombinationQuery()
    {
        var plan = Plan("{ span.a = 1 } || { name = \"y\" } | compare({ status = error } || { name = \"x\" })");

        Assert.IsType<SpanSetOr>(plan.Spansets);
        Assert.IsType<SpanSetOr>(plan.Compare!.Selection);
    }

    [Theory]
    [InlineData("{ } | compare({ rootName = \"x\" } || { name = \"y\" })")]
    [InlineData("{ } | compare({ } || { name = \"y\" })")]
    [InlineData("{ } | compare({ name = \"y\" } && { span.a = \"b\" } && { rootServiceName = \"x\" })")]
    public void Compare_OfACombination_NeedsASpanConditionInEverySpanset(string traceQl) =>
        Assert.Throws<TraceQlException>(() => Plan(traceQl));
}
