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
    public void NameOperators_MapToTheMatchMode()
    {
        Assert.Equal((SpanNameMatch.NotEquals, "GET /health"), Name("""{ name != "GET /health" }"""));
        // Tempo anchors patterns; ClickHouse match() does not, so the planner wraps them.
        Assert.Equal((SpanNameMatch.Regex, "^(?:GET .*)$"), Name("""{ name =~ "GET .*" }"""));
        Assert.Equal((SpanNameMatch.NotRegex, "^(?:health|ready)$"), Name("""{ name !~ "health|ready" }"""));
        Assert.Equal((SpanNameMatch.Equals, "x"), Name("""{ name = "x" }"""));

        static (SpanNameMatch, string?) Name(string q)
        {
            var condition = Assert.Single(Plan(q).Conditions);
            return (condition.SpanNameMatch, condition.SpanName);
        }
    }

    [Fact]
    public void StatusNotEqual_NegatesTheStatus()
    {
        var condition = Assert.Single(Plan("{ status != error }").Conditions);

        Assert.Equal("STATUS_CODE_ERROR", condition.StatusCode);
        Assert.True(condition.StatusCodeNegated);
        Assert.False(Assert.Single(Plan("{ status = error }").Conditions).StatusCodeNegated);
    }

    [Theory]
    [InlineData("unspecified", 0)]
    [InlineData("internal", 1)]
    [InlineData("server", 2)]
    [InlineData("client", 3)]
    [InlineData("producer", 4)]
    [InlineData("consumer", 5)]
    public void Kind_MapsToTheOtelNumber(string word, int number)
    {
        var equal = Assert.Single(Plan($"{{ kind = {word} }}").Conditions);
        var notEqual = Assert.Single(Plan($"{{ kind != {word} }}").Conditions);

        Assert.Equal((byte)number, equal.Kind);
        Assert.False(equal.KindNegated);
        Assert.Equal((byte)number, notEqual.Kind);
        Assert.True(notEqual.KindNegated);
        Flare.Api.Query.TraceStructureSqlBuilder.Validate(Plan($"{{ kind = {word} }}").Structure!);
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
    [InlineData("{ statusMessage = \"x\" }", "not supported")]
    [InlineData("{ status = \"weird\" }", "unknown status")]
    [InlineData("{ duration = 5 }", "duration")]
    [InlineData("{ duration =~ 5s }", "supports")]
    [InlineData("{ name = \"a\" && name = \"b\" }", "twice")]
    [InlineData("{ name = \"a\" && name != \"a\" }", "twice")]
    [InlineData("{ kind = server && kind = client }", "twice")]
    [InlineData("{ status = error && status != error }", "twice")]
    [InlineData("{ kind > server }", "supports")]
    [InlineData("{ kind = \"server\" }", "unspecified, internal")]
    [InlineData("{ name > \"a\" }", "supports")]
    [InlineData("{ name =~ 5 }", "quoted pattern")]
    [InlineData("{ status > error }", "supports")]
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

    [Theory]
    [InlineData("{ nestedSetParent < 0 }", true)]
    [InlineData("{ nestedSetParent <= -1 }", true)]
    [InlineData("{ nestedSetParent = -1 }", true)]
    [InlineData("{ nestedSetParent<0 && true }", true)]
    [InlineData("{ nestedSetParent > 0 }", false)]
    [InlineData("{ nestedSetParent >= 1 }", false)]
    [InlineData("{ nestedSetParent != -1 }", false)]
    [InlineData("{ nestedSetParent > -1 }", false)]
    [InlineData("{ nestedSetParent = 3 }", false)]
    public void NestedSetParent_SelectsRootOrNonRootSpans(string traceQl, bool root)
    {
        var condition = Assert.Single(Plan(traceQl).Conditions);

        Assert.Equal(root, condition.RootSpan);
    }

    [Theory]
    [InlineData("{ nestedSetParent < 100 }")]
    [InlineData("{ nestedSetParent >= -1 }")]
    [InlineData("{ nestedSetParent != 0 }")]
    public void NestedSetParent_TheEverySpanCanSatisfy_AddsNoRestriction(string traceQl) => Assert.Empty(Plan(traceQl).Conditions);

    [Theory]
    [InlineData("{ nestedSetParent < -1 }")]
    [InlineData("{ nestedSetParent > 0 && nestedSetParent < 0 }")]
    public void NestedSetParent_NoSpanCanSatisfy_IsRejected(string traceQl) => Assert.Throws<TraceQlException>(() => Plan(traceQl));

    [Fact]
    public void NestedSetParent_NeedsAWholeNumber() => Assert.Throws<TraceQlException>(() => Plan("{ nestedSetParent < 0.5 }"));

    [Fact]
    public void RootSpanAlone_IsAConditionOfItsOwn_AndCombinesWithOthers()
    {
        var condition = Assert.Single(Plan("{ nestedSetParent<0 && true && status = error }").Conditions);

        Assert.True(condition.RootSpan);
        Assert.Equal("STATUS_CODE_ERROR", condition.StatusCode);
    }

    [Fact]
    public void NotNil_IsAttributePresence_AndNil_IsAbsence()
    {
        var present = Assert.Single(Assert.Single(Plan("{ resource.service.name != nil }").Conditions).Attributes!);
        var absent = Assert.Single(Assert.Single(Plan("{ span.http.route = nil }").Conditions).Attributes!);

        Assert.Equal((SpanAttributeBag.Resource, "service.name", SpanAttributeFilterOperator.Exists), (present.Bag, present.Key, present.Operator));
        Assert.Equal((SpanAttributeBag.Span, "http.route", SpanAttributeFilterOperator.Absent), (absent.Bag, absent.Key, absent.Operator));
    }

    [Theory]
    [InlineData("{ span.x > nil }")]
    [InlineData("{ span.x =~ nil }")]
    public void Nil_OnlyWorksWithEqualityOperators(string traceQl) => Assert.Throws<TraceQlException>(() => Plan(traceQl));
}
