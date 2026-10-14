using Flare.Api.Model;
using Flare.Api.Tempo;
using Xunit;

namespace Flare.Api.Tests.Tempo;

public class TempoSearchPlannerTests
{
    private static TempoSearchPlan Plan(string traceQl) => TempoSearchPlanner.Plan(TraceQlParser.Parse(traceQl));

    [Fact]
    public void EmptySpanset_MatchesEveryTrace()
    {
        var plan = Plan("{ }");

        Assert.Null(plan.Structure);
        Assert.Empty(plan.RootServices);
    }

    [Fact]
    public void SpanIntrinsicsAndService_FillOneCondition()
    {
        var plan = Plan("""{ resource.service.name = "checkout" && name = "POST /pay" && status = error && duration >= 100ms && duration < 2s }""");

        var condition = Assert.Single(plan.Conditions);
        Assert.Equal("A", condition.Name);
        Assert.Equal("checkout", condition.ServiceName);
        Assert.Equal("POST /pay", condition.SpanName);
        Assert.Equal("STATUS_CODE_ERROR", condition.StatusCode);
        Assert.Equal(100_000_000UL, condition.MinDurationNano);
        Assert.Equal(1_999_999_999UL, condition.MaxDurationNano);
        Assert.Equal("A", plan.Structure!.Expression);
    }

    [Fact]
    public void StrictDurationBounds_AreOneNanosecondInside()
    {
        var plan = Plan("{ duration > 1s && duration <= 3s }");

        var condition = Assert.Single(plan.Conditions);
        Assert.Equal(1_000_000_001UL, condition.MinDurationNano);
        Assert.Equal(3_000_000_000UL, condition.MaxDurationNano);
    }

    [Fact]
    public void Attributes_MapToTheirBagAndOperator()
    {
        var plan = Plan("""{ span.http.method = "GET" && span.http.route != "/health" && resource.k8s.pod.name =~ "api-.*" && span.http.status_code >= 500 && span.http.target !~ "/static" }""");

        var attributes = Assert.Single(plan.Conditions).Attributes!;
        Assert.Collection(
            attributes,
            a => Assert.Equal((SpanAttributeBag.Span, "http.method", "GET", SpanAttributeFilterOperator.Equals), (a.Bag, a.Key, a.Value, a.Operator)),
            a => Assert.Equal((SpanAttributeBag.Span, "http.route", "/health", SpanAttributeFilterOperator.NotEquals), (a.Bag, a.Key, a.Value, a.Operator)),
            // Tempo anchors patterns; ClickHouse match() does not, so the planner wraps them.
            a => Assert.Equal((SpanAttributeBag.Resource, "k8s.pod.name", "^(?:api-.*)$", SpanAttributeFilterOperator.Regex), (a.Bag, a.Key, a.Value, a.Operator)),
            a => Assert.Equal((SpanAttributeBag.Span, "http.status_code", "500", SpanAttributeFilterOperator.GreaterThanOrEqual), (a.Bag, a.Key, a.Value, a.Operator)),
            a => Assert.Equal((SpanAttributeBag.Span, "http.target", "^(?:/static)$", SpanAttributeFilterOperator.NotRegex), (a.Bag, a.Key, a.Value, a.Operator)));
    }

    [Fact]
    public void ServiceNameThatIsNotAnEquality_StaysAResourceAttribute()
    {
        var plan = Plan("""{ resource.service.name =~ "api.*" }""");

        var condition = Assert.Single(plan.Conditions);
        Assert.Null(condition.ServiceName);
        var attribute = Assert.Single(condition.Attributes!);
        Assert.Equal((SpanAttributeBag.Resource, "service.name"), (attribute.Bag, attribute.Key));
    }

    [Theory]
    [InlineData(".service.name", SpanAttributeBag.Resource)]
    [InlineData(".k8s.namespace.name", SpanAttributeBag.Resource)]
    [InlineData(".http.method", SpanAttributeBag.Span)]
    [InlineData(".order.id", SpanAttributeBag.Span)]
    public void UnscopedAttributes_ReadTheResourceBagOnlyInResourceNamespaces(string field, SpanAttributeBag expected)
    {
        var plan = Plan($"{{ {field} != \"x\" }}");

        Assert.Equal(expected, Assert.Single(Assert.Single(plan.Conditions).Attributes!).Bag);
    }

    [Fact]
    public void TraceLevelIntrinsics_FilterTheRootRowNotACondition()
    {
        var plan = Plan("""{ rootServiceName = "gateway" && rootName = "GET /" && traceDuration > 500ms && traceDuration <= 2s }""");

        Assert.Null(plan.Structure);
        Assert.Equal(["gateway"], plan.RootServices);
        Assert.Equal(["GET /"], plan.RootNames);
        Assert.Equal(500_000_001UL, plan.MinTraceDurationNano);
        Assert.Equal(2_000_000_000UL, plan.MaxTraceDurationNano);
    }

    [Fact]
    public void TraceLevelIntrinsicsAlongsideSpanPredicates_ApplyToTheTraceAndTheSpanSeparately()
    {
        var plan = Plan("""{ rootServiceName = "gateway" && span.db.system = "postgresql" }""");

        Assert.Equal(["gateway"], plan.RootServices);
        Assert.Equal("db.system", Assert.Single(Assert.Single(plan.Conditions).Attributes!).Key);
    }

    [Fact]
    public void MultipleSpansets_BecomeLetteredConditionsWithTheExpression()
    {
        var plan = Plan("""{ resource.service.name = "checkout" } >> { resource.service.name = "payment" && status = error }""");

        Assert.Equal(["A", "B"], plan.Conditions.Select(c => c.Name));
        Assert.Equal("payment", plan.Conditions[1].ServiceName);
        Assert.Equal("A => B", plan.Structure!.Expression);
        // The structural query builder must accept the filter as is.
        Flare.Api.Query.TraceStructureSqlBuilder.Validate(plan.Structure);
    }

    [Theory]
    [InlineData("{ rootName = \"x\" } || { name = \"y\" }", "single spanset")]
    [InlineData("{ } && { name = \"y\" }", "empty spanset")]
    [InlineData("{ kind = server }", "not supported")]
    [InlineData("{ statusMessage = \"x\" }", "not supported")]
    [InlineData("{ name != \"x\" }", "only =")]
    [InlineData("{ name =~ \"x.*\" }", "only =")]
    [InlineData("{ status != error }", "only =")]
    [InlineData("{ status = \"weird\" }", "unknown status")]
    [InlineData("{ duration = 5 }", "duration")]
    [InlineData("{ duration =~ 5s }", "supports")]
    [InlineData("{ name = \"a\" && name = \"b\" }", "twice")]
    [InlineData("{ http.method = \"GET\" }", "not a field")]
    [InlineData("{ span. = \"x\" }", "no attribute name")]
    [InlineData("{ span.x > \"a\" }", "against a number")]
    [InlineData("{ span.x = 5s }", "duration")]
    [InlineData("{ span.x =~ 5 }", "quoted pattern")]
    public void UnsupportedConstructs_AreRejected(string traceQl, string messagePart)
    {
        var ex = Assert.Throws<TraceQlException>(() => Plan(traceQl));

        Assert.Contains(messagePart, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tags_AreAnAndOfEqualitiesOnOneSpan()
    {
        var plan = TempoSearchPlanner.PlanTags("""service.name=checkout http.method=GET name="POST /pay" status=error root.name=front""");

        var condition = Assert.Single(plan.Conditions);
        Assert.Equal("checkout", condition.ServiceName);
        Assert.Equal("POST /pay", condition.SpanName);
        Assert.Equal("STATUS_CODE_ERROR", condition.StatusCode);
        Assert.Equal("http.method", Assert.Single(condition.Attributes!).Key);
        Assert.Equal(["front"], plan.RootNames);
    }

    [Fact]
    public void Tags_AcceptExplicitScopes()
    {
        var plan = TempoSearchPlanner.PlanTags("resource.cloud.region=eu span.cloud.region=us");

        var attributes = Assert.Single(plan.Conditions).Attributes!;
        Assert.Equal([SpanAttributeBag.Resource, SpanAttributeBag.Span], attributes.Select(a => a.Bag));
    }

    [Fact]
    public void NoTags_MatchesEveryTrace() => Assert.Null(TempoSearchPlanner.PlanTags(null).Structure);

    [Theory]
    [InlineData("novalue")]
    [InlineData("=x")]
    [InlineData("a=\"open")]
    public void MalformedTags_AreRejected(string tags) => Assert.Throws<TraceQlException>(() => TempoSearchPlanner.PlanTags(tags));

    [Fact]
    public void DurationParameters_TightenTheTraceBounds()
    {
        var plan = TempoSearchPlanner.WithDurations(Plan("{ traceDuration > 100ms }"), "250ms", "3s");

        Assert.Equal(250_000_000UL, plan.MinTraceDurationNano);
        Assert.Equal(3_000_000_000UL, plan.MaxTraceDurationNano);
    }

    [Fact]
    public void InvalidDurationParameter_IsRejected() =>
        Assert.Throws<TraceQlException>(() => TempoSearchPlanner.WithDurations(new TempoSearchPlan(), "soon", null));
}
