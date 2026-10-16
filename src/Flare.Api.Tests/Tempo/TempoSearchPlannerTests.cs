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
    [InlineData("{ duration = \"5\" }", "duration")]
    [InlineData("{ duration > -1 }", "duration")]
    [InlineData("{ duration =~ 5s }", "supports")]
    [InlineData("{ name = \"a\" && name = \"b\" }", "no span")]
    [InlineData("{ name = \"a\" && name != \"a\" }", "no span")]
    [InlineData("{ kind = server && kind = client }", "no span")]
    [InlineData("{ status = error && status != error }", "no span")]
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
    [Fact]
    public void SelectFields_AreResolvedLikeFilters()
    {
        var plan = Plan("{ status = error } | select(status, nestedSetLeft, resource.service.name, span.http.route, .k8s.pod.name, .http.method, event.exception.type)");

        Assert.Equal(
            [
                new TempoSelectField(TempoSelectScope.Intrinsic, "status"),
                new TempoSelectField(TempoSelectScope.Intrinsic, "nestedSetLeft"),
                new TempoSelectField(TempoSelectScope.Resource, "service.name"),
                new TempoSelectField(TempoSelectScope.Span, "http.route"),
                new TempoSelectField(TempoSelectScope.Resource, "k8s.pod.name"),
                new TempoSelectField(TempoSelectScope.Span, "http.method"),
                new TempoSelectField(TempoSelectScope.Event, "exception.type"),
            ],
            plan.Select);
        Assert.True(plan.UsesSelector);
        Assert.True(plan.NeedsWholeTrace);
    }

    [Fact]
    public void ASelectWithoutNestedSets_DoesNotNeedTheWholeTrace()
    {
        var plan = Plan("{ status = error } | select(status, event.exception.message)");

        Assert.True(plan.UsesSelector);
        Assert.False(plan.NeedsWholeTrace);
    }

    [Fact]
    public void APlainQuery_DoesNotUseTheSelector() => Assert.False(Plan("{ status = error }").UsesSelector);

    [Fact]
    public void UnionDescendant_UsesTheSelectorAndNeedsTheWholeTrace()
    {
        var plan = Plan("({ nestedSetParent < 0 && true } &>> { kind = server }) || ({ nestedSetParent < 0 && true })");

        Assert.Equal("(A => B || C)", plan.Structure!.Expression);
        Assert.Equal(new TraceQlUnionRelation('A', 'B'), Assert.Single(plan.UnionDescendants));
        Assert.True(plan.UsesSelector);
        Assert.True(plan.NeedsWholeTrace);
        Assert.Equal(["A", "B", "C"], plan.Conditions.Select(c => c.Name));
        Assert.True(plan.Conditions[0].RootSpan);
        Flare.Api.Query.TraceStructureSqlBuilder.Validate(plan.Structure);
    }

    [Theory]
    [InlineData("{ nestedSetParent < 0 } &>> { true }", 1)]
    [InlineData("{ } >> { name = \"b\" }", 0)]
    [InlineData("{ name = \"a\" } > { }", 1)]
    public void AnEmptySideOfARelation_IsAnySpan(string traceQl, int emptyIndex)
    {
        var plan = Plan(traceQl);

        Assert.Equal(2, plan.Conditions.Count);
        Assert.Equal(0UL, plan.Conditions[emptyIndex].MinDurationNano);
        Flare.Api.Query.TraceStructureSqlBuilder.Validate(plan.Structure!);
    }

    [Fact]
    public void EventAttributes_FilterOnTheEventBag()
    {
        var plan = Plan("""{ status = error && event.exception.message = "boom" && event.exception.type != nil }""");

        var attributes = plan.Conditions[0].Attributes!;
        Assert.Equal(new SpanAttributeFilter { Bag = SpanAttributeBag.Event, Key = "exception.message", Value = "boom", Operator = SpanAttributeFilterOperator.Equals }, attributes[0]);
        Assert.Equal(SpanAttributeBag.Event, attributes[1].Bag);
        Assert.Equal(SpanAttributeFilterOperator.Exists, attributes[1].Operator);
    }

    [Fact]
    public void ADurationComparedWithANumber_IsInNanoseconds()
    {
        var plan = Plan("{ duration >= 0 && duration < 1500 }");

        Assert.Equal(0UL, plan.Conditions[0].MinDurationNano);
        Assert.Equal(1499UL, plan.Conditions[0].MaxDurationNano);
    }

    [Fact]
    public void ServicesInAnOrGroup_BecomeAListAndOneServiceStaysTheColumnTest()
    {
        var many = Assert.Single(Plan("""{ (resource.service.name = "a" || resource.service.name = "b") }""").Conditions);
        Assert.Null(many.ServiceName);
        Assert.Equal(new[] { "a", "b" }, many.ServiceNames);

        var one = Assert.Single(Plan("""{ (resource.service.name = "a" || resource.service.name = "a") }""").Conditions);
        Assert.Equal("a", one.ServiceName);
        Assert.Null(one.ServiceNames);
    }

    [Fact]
    public void AServiceAndAListOfServices_AreTheirIntersection()
    {
        var admitted = Assert.Single(Plan("""{ resource.service.name = "a" && (resource.service.name = "a" || resource.service.name = "b") }""").Conditions);
        Assert.Equal("a", admitted.ServiceName);
        Assert.Null(admitted.ServiceNames);

        Assert.Contains("no span satisfies", Assert.Throws<TraceQlException>(
            () => Plan("""{ resource.service.name = "c" && (resource.service.name = "a" || resource.service.name = "b") }""")).Message);
    }

    [Fact]
    public void NamesInAnOrGroup_AreOneAnchoredAlternationWithMetacharactersEscaped()
    {
        var condition = Assert.Single(Plan("""{ (name = "GET /a.b" || name = "POST (x)") }""").Conditions);

        Assert.Equal(SpanNameMatch.Regex, condition.SpanNameMatch);
        Assert.Equal("""^(?:GET /a\.b|POST \(x\))$""", condition.SpanName);
    }

    [Fact]
    public void SeveralNotEqualsOnTheName_AreOneNegatedAlternation()
    {
        var condition = Assert.Single(Plan("""{ (name != "a" && name != "b") }""").Conditions);

        Assert.Equal(SpanNameMatch.NotRegex, condition.SpanNameMatch);
        Assert.Equal("^(?:a|b)$", condition.SpanName);
    }

    [Fact]
    public void NamesEqualAndNotEqual_LeaveWhatIsEqualOnly()
    {
        var condition = Assert.Single(Plan("""{ (name = "a" || name = "b") && name != "a" }""").Conditions);

        Assert.Equal((SpanNameMatch.Equals, "b"), (condition.SpanNameMatch, condition.SpanName));
        Assert.Contains("no span name", Assert.Throws<TraceQlException>(() => Plan("""{ name = "a" && name = "b" }""")).Message);
        Assert.Contains("compared twice", Assert.Throws<TraceQlException>(() => Plan("""{ name =~ "a.*" && name != "abc" }""")).Message);
    }

    [Theory]
    [InlineData("{ status = error || status = ok }", "STATUS_CODE_UNSET", true)]
    [InlineData("{ status != ok && status != unset }", "STATUS_CODE_ERROR", false)]
    [InlineData("{ status != ok }", "STATUS_CODE_OK", true)]
    [InlineData("{ (status = error || status = ok) && status != ok }", "STATUS_CODE_ERROR", false)]
    public void StatusSets_ResolveToOneLabelPossiblyNegated(string traceQl, string label, bool negated)
    {
        var condition = Assert.Single(Plan(traceQl).Conditions);

        Assert.Equal(label, condition.StatusCode);
        Assert.Equal(negated, condition.StatusCodeNegated);
    }

    [Fact]
    public void StatusSetsThatCoverEverythingOrNothing_AreNoTestOrAnError()
    {
        Assert.Empty(Plan("{ status = error || status = ok || status = unset }").Conditions);
        Assert.Contains("no span satisfies", Assert.Throws<TraceQlException>(() => Plan("{ status = error && status = ok }")).Message);
    }

    [Fact]
    public void KindSets_AreOneValueAllButOneOrNoTest()
    {
        Assert.Equal((byte)2, Assert.Single(Plan("{ kind = server || kind = server }").Conditions).Kind);
        var allButServer = Assert.Single(Plan("{ kind != server && kind != client && kind != internal && kind != producer && kind != consumer }").Conditions);
        Assert.Equal((byte)0, allButServer.Kind);
        Assert.False(allButServer.KindNegated);
    }

    [Theory]
    [InlineData("{ kind = server || kind = client }", new byte[] { 2, 3 })]
    [InlineData("{ (kind = client || kind = server || kind = producer) }", new byte[] { 2, 3, 4 })]
    [InlineData("{ kind != internal && kind != unspecified }", new byte[] { 2, 3, 4, 5 })]
    [InlineData("{ kind != server && kind != client && kind != internal && kind != unspecified }", new byte[] { 4, 5 })]
    public void KindSetsOfTwoToFour_AreAMembershipTest(string traceQl, byte[] expected)
    {
        var condition = Assert.Single(Plan(traceQl).Conditions);

        Assert.Equal(expected, condition.Kinds);
        Assert.Null(condition.Kind);
        Assert.False(condition.KindNegated);
    }

    [Fact]
    public void KindSet_NarrowedByAnotherTest_ShrinksToTheIntersection()
    {
        var condition = Assert.Single(Plan("{ (kind = server || kind = client) && kind != client }").Conditions);

        Assert.Equal((byte)2, condition.Kind);
        Assert.Null(condition.Kinds);
    }

    [Fact]
    public void AttributesInAnOrGroup_AreAMembershipTest()
    {
        var attribute = Assert.Single(Assert.Single(Plan("{ (span.http.status_code = 500 || span.http.status_code = 503) }").Conditions).Attributes!);

        Assert.Equal(SpanAttributeFilterOperator.In, attribute.Operator);
        Assert.Equal(new[] { "500", "503" }, attribute.Values);
        Assert.Equal("http.status_code", attribute.Key);
    }

    [Fact]
    public void RootNamesAndServicesInAnOrGroup_JoinTheTraceLevelLists()
    {
        var plan = Plan("""{ (rootName = "a" || rootName = "b") && (rootServiceName = "x" || rootServiceName = "y") }""");

        Assert.Equal(new[] { "a", "b" }, plan.RootNames);
        Assert.Equal(new[] { "x", "y" }, plan.RootServices);
    }

    [Fact]
    public void AnOrGroupOfADuration_IsRejectedNamingTheFieldsThatTakeOne() =>
        Assert.Contains("not 'duration'", Assert.Throws<TraceQlException>(() => Plan("{ duration = 1ms || duration = 2ms }")).Message);

    [Fact]
    public void ScopedIntrinsics_AreTheUnscopedOnes()
    {
        var scoped = Plan("""{ span:name = "a" && span:status = error && span:duration > 1ms && span:kind = server && trace:rootService = "r" && trace:rootName = "n" && trace:duration > 2s }""");
        var plain = Plan("""{ name = "a" && status = error && duration > 1ms && kind = server && rootServiceName = "r" && rootName = "n" && traceDuration > 2s }""");

        Assert.Equal(plain.Conditions[0], scoped.Conditions[0]);
        Assert.Equal(plain.RootServices, scoped.RootServices);
        Assert.Equal(plain.RootNames, scoped.RootNames);
        Assert.Equal(plain.MinTraceDurationNano, scoped.MinTraceDurationNano);
    }

    [Fact]
    public void ScopedIntrinsics_WorkInSelectAndEventNameAndGrouping()
    {
        Assert.Equal("exception", Assert.Single(Assert.Single(Plan("""{ event:name = "exception" }""").Conditions).Attributes!).Value);
        Assert.Equal(TempoSelectScope.Intrinsic, Assert.Single(Plan("{ } | select(span:status)").Select).Scope);
    }

    [Theory]
    [InlineData("{ span:statusMessage = \"x\" }")]
    [InlineData("{ span:childCount > 1 }")]
    public void ScopedIntrinsicsWithNoCounterpart_AreNamedAsUnsupported(string traceQl) =>
        Assert.Contains("is not supported", Assert.Throws<TraceQlException>(() => Plan(traceQl)).Message);

    [Theory]
    [InlineData("span:id", "SpanId", "00000000000000ab")]
    [InlineData("span:parentID", "ParentSpanId", "00000000000000ab")]
    [InlineData("trace:id", "TraceId", "000000000000000000000000000000ab")]
    [InlineData("link:spanID", "Links.SpanId", "00000000000000ab")]
    [InlineData("link:traceID", "Links.TraceId", "000000000000000000000000000000ab")]
    public void IdIntrinsics_MapToTheirColumn_LowerCasedAndLeftPadded(string field, string column, string expected)
    {
        var attribute = Assert.Single(Assert.Single(Plan($"{{ {field} = \"AB\" }}").Conditions).Attributes!);

        Assert.Equal(SpanAttributeBag.Intrinsic, attribute.Bag);
        Assert.Equal(column, attribute.Key);
        Assert.Equal(SpanAttributeFilterOperator.Equals, attribute.Operator);
        Assert.Equal(expected, attribute.Value);
    }

    [Fact]
    public void IdIntrinsic_LongerThanAnIdOrNotHex_IsLeftAsWritten()
    {
        Assert.Equal("not-hex", Assert.Single(Assert.Single(Plan("""{ span:id = "not-hex" }""").Conditions).Attributes!).Value);
        var tooLong = new string('a', 17);
        Assert.Equal(tooLong, Assert.Single(Assert.Single(Plan($$"""{ span:id = "{{tooLong}}" }""").Conditions).Attributes!).Value);
    }

    [Theory]
    [InlineData("{ span:id != \"ab\" }", SpanAttributeFilterOperator.NotEquals)]
    [InlineData("{ span:id =~ \"ab.*\" }", SpanAttributeFilterOperator.Regex)]
    [InlineData("{ span:id !~ \"ab.*\" }", SpanAttributeFilterOperator.NotRegex)]
    [InlineData("{ span:parentID = nil }", SpanAttributeFilterOperator.Absent)]
    [InlineData("{ span:parentID != nil }", SpanAttributeFilterOperator.Exists)]
    public void IdIntrinsics_TakeTheTextOperatorsAndNil(string traceQl, SpanAttributeFilterOperator expected) =>
        Assert.Equal(expected, Assert.Single(Assert.Single(Plan(traceQl).Conditions).Attributes!).Operator);

    [Fact]
    public void IdIntrinsic_RegexIsAnchored_AndNotNormalised() =>
        Assert.Equal("^(?:AB.*)$", Assert.Single(Assert.Single(Plan("""{ span:id =~ "AB.*" }""").Conditions).Attributes!).Value);

    [Fact]
    public void IdIntrinsic_OrGroup_IsAMembershipTestOfNormalisedIds()
    {
        var attribute = Assert.Single(Assert.Single(Plan("""{ (trace:id = "A" || trace:id = "b") }""").Conditions).Attributes!);

        Assert.Equal(SpanAttributeFilterOperator.In, attribute.Operator);
        Assert.Equal(new[] { new string('0', 31) + "a", new string('0', 31) + "b" }, attribute.Values);
    }

    [Theory]
    [InlineData("{ span:id > \"a\" }", "supports =, !=, =~ and !~")]
    [InlineData("{ span:id = 5 }", "needs a quoted string")]
    [InlineData("{ span:id < nil }", "nil using = or !=")]
    public void IdIntrinsics_RejectOtherOperatorsAndValues(string traceQl, string message) =>
        Assert.Contains(message, Assert.Throws<TraceQlException>(() => Plan(traceQl)).Message);

    [Theory]
    [InlineData("instrumentation:name", "ScopeName")]
    [InlineData("instrumentation:version", "ScopeVersion")]
    public void InstrumentationIntrinsics_AreTheScopeColumns_AsWritten(string field, string column)
    {
        var attribute = Assert.Single(Assert.Single(Plan($$"""{ {{field}} = "Ab" }""").Conditions).Attributes!);

        Assert.Equal(column, attribute.Key);
        Assert.Equal("Ab", attribute.Value);
    }

    [Theory]
    [InlineData("{ event:timeSinceStart > 1ms }", SpanAttributeFilterOperator.GreaterThan, "1000000")]
    [InlineData("{ event:timeSinceStart <= 2s }", SpanAttributeFilterOperator.LessThanOrEqual, "2000000000")]
    [InlineData("{ event:timeSinceStart = 500 }", SpanAttributeFilterOperator.Equals, "500")]
    [InlineData("{ event:timeSinceStart != 1us }", SpanAttributeFilterOperator.NotEquals, "1000")]
    public void EventTimeSinceStart_IsADurationInNanoseconds(string traceQl, SpanAttributeFilterOperator op, string nanos)
    {
        var attribute = Assert.Single(Assert.Single(Plan(traceQl).Conditions).Attributes!);

        Assert.Equal(SpanAttributeBag.Intrinsic, attribute.Bag);
        Assert.Equal("Events.TimeSinceStart", attribute.Key);
        Assert.Equal(op, attribute.Operator);
        Assert.Equal(nanos, attribute.Value);
    }

    [Theory]
    [InlineData("{ event:timeSinceStart =~ \"x\" }", "supports =, !=, >")]
    [InlineData("{ event:timeSinceStart > \"x\" }", "must be compared with a duration")]
    [InlineData("{ event:timeSinceStart = 1ms || event:timeSinceStart = 2ms }", "not 'event:timeSinceStart'")]
    public void EventTimeSinceStart_RejectsWhatIsNotADurationComparison(string traceQl, string message) =>
        Assert.Contains(message, Assert.Throws<TraceQlException>(() => Plan(traceQl)).Message);
}
