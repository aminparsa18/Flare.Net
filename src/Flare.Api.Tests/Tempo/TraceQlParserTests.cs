using Flare.Api.Tempo;
using Xunit;

namespace Flare.Api.Tests.Tempo;

public class TraceQlParserTests
{
    [Fact]
    public void EmptySpanset_IsOneSpansetWithNoPredicates()
    {
        var query = TraceQlParser.Parse("{ }");

        var spanset = Assert.Single(query.Spansets);
        Assert.Empty(spanset.Predicates);
        Assert.Equal("A", query.Expression);
    }

    [Fact]
    public void Predicates_ReadFieldOperatorAndTypedValue()
    {
        var query = TraceQlParser.Parse("""{ resource.service.name = "api" && span.http.status_code >= 500 && duration > 1.5s && status = error && span.ok = true }""");

        var p = Assert.Single(query.Spansets).Predicates;
        Assert.Equal(5, p.Count);
        Assert.Equal(new TraceQlPredicate("resource.service.name", TraceQlOp.Eq, new TraceQlValue(TraceQlValueKind.String, "api")), p[0]);
        Assert.Equal(new TraceQlPredicate("span.http.status_code", TraceQlOp.Gte, new TraceQlValue(TraceQlValueKind.Number, "500")), p[1]);
        Assert.Equal(TraceQlValueKind.Duration, p[2].Value.Kind);
        Assert.Equal(1_500_000_000UL, p[2].Value.Nanos);
        Assert.Equal(new TraceQlValue(TraceQlValueKind.Status, "error"), p[3].Value);
        Assert.Equal(new TraceQlValue(TraceQlValueKind.Bool, "true"), p[4].Value);
    }

    [Theory]
    [InlineData("=", TraceQlOp.Eq)]
    [InlineData("!=", TraceQlOp.Neq)]
    [InlineData("=~", TraceQlOp.Re)]
    [InlineData("!~", TraceQlOp.Nre)]
    [InlineData(">", TraceQlOp.Gt)]
    [InlineData(">=", TraceQlOp.Gte)]
    [InlineData("<", TraceQlOp.Lt)]
    [InlineData("<=", TraceQlOp.Lte)]
    public void Operators_AreRecognisedWithoutSpaces(string op, TraceQlOp expected)
    {
        var query = TraceQlParser.Parse($"{{span.x{op}\"1\"}}");

        Assert.Equal(expected, Assert.Single(Assert.Single(query.Spansets).Predicates).Op);
    }

    [Fact]
    public void StringEscapesAndBackticks_AreDecoded()
    {
        var query = TraceQlParser.Parse("""{ name = "say \"hi\"\n" && span.re =~ `a\d+` }""");

        var p = Assert.Single(query.Spansets).Predicates;
        Assert.Equal("say \"hi\"\n", p[0].Value.Text);
        Assert.Equal(@"a\d+", p[1].Value.Text);
    }

    [Theory]
    [InlineData("{ name = \"a\" } && { name = \"b\" }", "(A && B)")]
    [InlineData("{ name = \"a\" } || { name = \"b\" }", "(A || B)")]
    [InlineData("{ name = \"a\" } > { name = \"b\" }", "A -> B")]
    [InlineData("{ name = \"a\" } >> { name = \"b\" }", "A => B")]
    [InlineData("{ name = \"a\" } >> { name = \"b\" } && { name = \"c\" }", "(A => B && C)")]
    [InlineData("({ name = \"a\" } || { name = \"b\" }) && { name = \"c\" }", "((A || B) && C)")]
    public void SpansetOperators_BecomeAStructureExpression(string traceQl, string expression)
    {
        var query = TraceQlParser.Parse(traceQl);

        Assert.Equal(expression, query.Expression);
        // The result must be something the structural trace filter itself accepts.
        Flare.Api.Query.TraceStructureExpression.Parse(query.Expression);
    }

    [Theory]
    [InlineData("{ } | count() > 2", "pipeline")]
    [InlineData("{ name = \"a\" } | avg(duration)", "pipeline")]
    [InlineData("{ span.a = 1 || span.b = 2 }", "||")]
    [InlineData("{ name = \"a\" } &&& { }", "unexpected")]
    [InlineData("{ name = }", "unexpected")]
    [InlineData("{ name }", "needs a comparison")]
    [InlineData("{ name = \"unterminated }", "not closed")]
    [InlineData("{ span.x = span.y }", "not a value")]
    [InlineData("{ duration > 5q }", "valid number or duration")]
    [InlineData("{ name = \"a\" ", "ends")]
    [InlineData("", "empty")]
    [InlineData("{ name = \"a\" } >> ({ name = \"b\" } && { name = \"c\" })", "relates two spansets")]
    public void UnsupportedOrMalformed_IsRejectedWithAMessageNamingIt(string traceQl, string messagePart)
    {
        var ex = Assert.Throws<TraceQlException>(() => TraceQlParser.Parse(traceQl));

        Assert.Contains(messagePart, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("250ms", 250_000_000UL)]
    [InlineData("2s", 2_000_000_000UL)]
    [InlineData("1m30s", 90_000_000_000UL)]
    [InlineData("1h", 3_600_000_000_000UL)]
    [InlineData("10us", 10_000UL)]
    [InlineData("10µs", 10_000UL)]
    [InlineData("5ns", 5UL)]
    [InlineData("0.5s", 500_000_000UL)]
    public void Durations_ParseToNanoseconds(string text, ulong nanos)
    {
        Assert.True(TraceQlParser.TryParseDuration(text, out var parsed));
        Assert.Equal(nanos, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("5")]
    [InlineData("s")]
    [InlineData("5x")]
    public void NotADuration_IsRejected(string text) => Assert.False(TraceQlParser.TryParseDuration(text, out _));

    [Theory]
    [InlineData("{ true }")]
    [InlineData("{ true && status = error }")]
    [InlineData("{ status = error && true }")]
    [InlineData("{ nestedSetParent<0 && true && status = error }")]
    public void ABareTrue_IsAPredicateThatChangesNothing(string traceQl)
    {
        var spanset = Assert.Single(TraceQlParser.Parse(traceQl).Spansets);

        Assert.DoesNotContain(spanset.Predicates, p => p.Field == "true");
    }

    [Fact]
    public void ABareFalse_IsRejected()
    {
        var ex = Assert.Throws<TraceQlException>(() => TraceQlParser.Parse("{ false }"));

        Assert.Contains("matches no span", ex.Message);
    }

    [Fact]
    public void TrueAsAValue_StillParses() =>
        Assert.Equal(TraceQlValueKind.Bool, Assert.Single(TraceQlParser.Parse("{ span.cached = true }").Spansets[0].Predicates).Value.Kind);

    [Fact]
    public void Nil_IsAValue() =>
        Assert.Equal(TraceQlValueKind.Nil, Assert.Single(TraceQlParser.Parse("{ span.x != nil }").Spansets[0].Predicates).Value.Kind);
    [Fact]
    public void SelectStage_ReadsTheFieldsInOrderWithoutRepeats()
    {
        var query = TraceQlParser.Parse("{ status = error } | select(status, resource.service.name, name, nestedSetLeft, event.exception.message, status) with(most_recent=true)");

        Assert.Equal(["status", "resource.service.name", "name", "nestedSetLeft", "event.exception.message"], query.Select);
    }

    [Fact]
    public void SelectStage_FollowsAParenthesizedExpression()
    {
        var query = TraceQlParser.Parse("({ a = 1 } || { b = 2 }) | select(duration)");

        Assert.Equal(2, query.Spansets.Count);
        Assert.Equal(["duration"], query.Select);
    }

    [Theory]
    [InlineData("{ } | count() > 1", "pipeline stages")]
    [InlineData("{ } | select(childCount)", "select(childCount)")]
    [InlineData("{ } | select(parent)", "select(parent)")]
    [InlineData("{ } | select()", "unexpected")]
    [InlineData("{ } | select(name) with(sample=true)", "most_recent")]
    [InlineData("{ } | select(name) with(most_recent=2)", "unexpected")]
    [InlineData("{ } | select(name) | select(status)", "unexpected")]
    public void SelectStage_RejectsWhatItCannotRead(string traceQl, string message) =>
        Assert.Contains(message, Assert.Throws<TraceQlException>(() => TraceQlParser.Parse(traceQl)).Message);

    [Fact]
    public void SelectStage_TakesStatusMessageAndTheTraceLevelFields()
    {
        var query = TraceQlParser.Parse("{ } | select(statusMessage, rootName, rootServiceName, traceDuration, span:statusMessage, trace:rootName)");

        Assert.Equal(["statusMessage", "rootName", "rootServiceName", "traceDuration"], query.Select);
    }

    [Fact]
    public void SelectStage_TakesAtMostTwentyFields()
    {
        var fields = string.Join(", ", Enumerable.Range(0, TraceQlParser.MaxSelectFields + 1).Select(i => $"span.f{i}"));

        Assert.Contains("at most", Assert.Throws<TraceQlException>(() => TraceQlParser.Parse($"{{ }} | select({fields})")).Message);
    }

    [Fact]
    public void UnionDescendant_IsADescendantRelationThatRemembersItsSides()
    {
        var query = TraceQlParser.Parse("({ a = 1 } &>> { b = 2 }) || ({ c = 3 })");

        Assert.Equal("(A => B || C)", query.Expression);
        Assert.Equal(new TraceQlUnionRelation('A', 'B'), Assert.Single(query.UnionDescendants));
    }

    [Fact]
    public void UnionDescendant_RelatesSpansetsNotGroups() =>
        Assert.Throws<TraceQlException>(() => TraceQlParser.Parse("({ a = 1 } && { b = 2 }) &>> { c = 3 }"));

    [Fact]
    public void UnionDescendant_IsRejectedInAMetricsQuery() =>
        Assert.Contains("&>>", Assert.Throws<TraceQlException>(() => TraceQlParser.ParseMetrics("{ a = 1 } &>> { b = 2 } | rate()")).Message);

    [Fact]
    public void AnOrGroupOfOneField_IsOnePredicateWithAlternatives()
    {
        var query = TraceQlParser.Parse("""{ (resource.service.name = "a" || resource.service.name = "b" || resource.service.name = "c") && status = error }""");

        var p = Assert.Single(query.Spansets).Predicates;
        Assert.Equal(2, p.Count);
        Assert.Equal("a", p[0].Value.Text);
        Assert.Equal(new[] { "b", "c" }, p[0].Alternatives!.Select(v => v.Text));
        Assert.Null(p[1].Alternatives);
    }

    [Fact]
    public void AnOrWithoutParentheses_IsTheSameGroup()
    {
        var p = Assert.Single(TraceQlParser.Parse("{ span.http.status_code = 500 || span.http.status_code = 503 }").Spansets).Predicates;

        Assert.Equal("500", Assert.Single(p).Value.Text);
        Assert.Equal("503", Assert.Single(p[0].Alternatives!).Text);
    }

    [Fact]
    public void AnAndGroup_IsItsPredicatesWrittenOut()
    {
        var p = Assert.Single(TraceQlParser.Parse("""{ (name != "a" && name != "b") && ((status = error)) }""").Spansets).Predicates;

        Assert.Equal(3, p.Count);
        Assert.All(p.Take(2), x => Assert.Equal(TraceQlOp.Neq, x.Op));
    }

    [Theory]
    [InlineData("""{ name = "a" || span.x = "b" }""")]
    [InlineData("""{ name = "a" || name != "b" }""")]
    [InlineData("""{ (name = "a" && span.x = "b") || name = "c" }""")]
    [InlineData("{ span.x = nil || span.x = \"a\" }")]
    public void AnOrOfAnythingButEqualitiesOfOneField_IsRejectedWithTheRule(string query) =>
        Assert.Contains("only combines '='", Assert.Throws<TraceQlException>(() => TraceQlParser.Parse(query)).Message);

    [Fact]
    public void AnOrGroupWithABareTrue_HoldsForEverySpan() =>
        Assert.Empty(Assert.Single(TraceQlParser.Parse("""{ true || name = "a" }""").Spansets).Predicates);

    [Fact]
    public void AnOrGroup_IsAlsoReadInAMetricsQuery()
    {
        var query = TraceQlParser.ParseMetrics("""{ (name = "a" || name = "b") } | rate()""");

        Assert.Single(query.Spanset.Predicates[0].Alternatives!);
    }
}
