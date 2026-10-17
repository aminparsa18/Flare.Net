using Flare.Api.Prometheus;
using Xunit;

namespace Flare.Api.Tests.Prometheus;

public class PromQlParserTests
{
    [Fact]
    public void Parse_SelectorWithMatchers()
    {
        var expr = Assert.IsType<PromSelector>(PromQlParser.Parse("""http_requests{job="api", code!="200", path=~"/a.*"}"""));

        Assert.Equal("http_requests", expr.EffectiveMetricName);
        Assert.Equal(
            [(PromMatchOp.Equal, "job"), (PromMatchOp.NotEqual, "code"), (PromMatchOp.Regex, "path")],
            expr.Matchers.Select(m => (m.Op, m.Name)));
    }

    [Fact]
    public void Parse_NameMatcherOnly_ResolvesMetricName()
    {
        var expr = Assert.IsType<PromSelector>(PromQlParser.Parse("""{__name__="up"}"""));
        Assert.Equal("up", expr.EffectiveMetricName);
    }

    [Fact]
    public void Parse_RateOverRangeSelector()
    {
        var expr = Assert.IsType<PromRangeFunction>(PromQlParser.Parse("rate(http_requests_total[5m])"));

        Assert.Equal("rate", expr.Name);
        Assert.Equal(TimeSpan.FromMinutes(5), expr.Selector.Range);
    }

    [Theory]
    [InlineData("sum by (job) (rate(x[1m]))", false, new[] { "job" })]
    [InlineData("sum(rate(x[1m])) by (job, code)", false, new[] { "job", "code" })]
    [InlineData("max without (instance) (rate(x[1m]))", true, new[] { "instance" })]
    [InlineData("count(x)", false, new string[0])]
    public void Parse_Aggregation(string query, bool without, string[] labels)
    {
        var agg = Assert.IsType<PromAggregation>(PromQlParser.Parse(query));

        Assert.Equal(without, agg.Without);
        Assert.Equal(labels, agg.Labels);
    }

    [Fact]
    public void Parse_HistogramQuantile()
    {
        var expr = Assert.IsType<PromHistogramQuantile>(
            PromQlParser.Parse("histogram_quantile(0.95, sum by (le, route) (rate(d_bucket[5m])))"));

        Assert.Equal(0.95, expr.Quantile);
        Assert.IsType<PromAggregation>(expr.Inner);
    }

    [Fact]
    public void Parse_ScalarArithmetic_IsFolded()
    {
        Assert.Equal(7, Assert.IsType<PromNumber>(PromQlParser.Parse("1 + 2 * 3")).Value);
        Assert.Equal(2, Assert.IsType<PromNumber>(PromQlParser.Parse("1+1")).Value);
    }

    [Theory]
    [InlineData("label_replace(x, \"a\", \"b\", \"c\", \"d\")", "not supported")]
    [InlineData("x offset -5m", "negative offset")]
    [InlineData("a and 1", "scalar expression")]
    [InlineData("1 > 2", "bool")]
    [InlineData("a + bool b", "only valid on comparison")]
    [InlineData("a and on(x) group_left b", "no grouping allowed")]
    [InlineData("abs(1)", "scalar")]
    [InlineData("abs(x[5m])", "range selector")]
    [InlineData("a + x[5m]", "range selector")]
    [InlineData("topk(x, y)", "number literal")]
    [InlineData("clamp(x, 1)", "argument")]
    [InlineData("x @ 100", "@")]
    [InlineData("rate(x)", "range selector")]
    [InlineData("x[5m]", "range selector")]
    [InlineData("rate(x[5m:1m])", "subqueries")]
    [InlineData("sum(", "unexpected end")]
    [InlineData("", "empty")]
    [InlineData("{}", "at least one")]
    public void Parse_UnsupportedOrMalformed_ThrowsNamingTheProblem(string query, string fragment)
    {
        var ex = Assert.Throws<PromQlException>(() => PromQlParser.Parse(query));
        Assert.Contains(fragment, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("a / b", "/", false)]
    [InlineData("rate(x[5m]) * 100", "*", false)]
    [InlineData("100 * a", "*", false)]
    [InlineData("a > b", ">", false)]
    [InlineData("a > bool b", ">", true)]
    [InlineData("a >= 1", ">=", false)]
    [InlineData("a and b", "and", false)]
    [InlineData("a unless b", "unless", false)]
    [InlineData("a or b", "or", false)]
    [InlineData("a ^ 2", "^", false)]
    public void Parse_BinaryOperators(string query, string op, bool returnBool)
    {
        var binary = Assert.IsType<PromBinary>(PromQlParser.Parse(query));

        Assert.Equal(op, binary.Op);
        Assert.Equal(returnBool, binary.ReturnBool);
    }

    [Fact]
    public void Parse_BinaryPrecedence_MultiplicationBindsTighterThanAddition()
    {
        var binary = Assert.IsType<PromBinary>(PromQlParser.Parse("a + b * c"));

        Assert.Equal("+", binary.Op);
        Assert.Equal("*", Assert.IsType<PromBinary>(binary.Right).Op);
    }

    [Fact]
    public void Parse_Power_IsRightAssociative_AndBindsTighterThanUnaryMinus()
    {
        Assert.Equal(512, Assert.IsType<PromNumber>(PromQlParser.Parse("2 ^ 3 ^ 2")).Value);
        Assert.Equal(-4, Assert.IsType<PromNumber>(PromQlParser.Parse("-2 ^ 2")).Value);
    }

    [Fact]
    public void Parse_UnaryMinusOnVector_IsMultiplicationByMinusOne()
    {
        var binary = Assert.IsType<PromBinary>(PromQlParser.Parse("-x"));

        Assert.Equal("*", binary.Op);
        Assert.Equal(-1, Assert.IsType<PromNumber>(binary.Left).Value);
    }

    [Fact]
    public void Parse_SetOperatorsBindLooserThanComparisons()
    {
        var binary = Assert.IsType<PromBinary>(PromQlParser.Parse("a > 1 and b > 2"));

        Assert.Equal("and", binary.Op);
        Assert.Equal(">", Assert.IsType<PromBinary>(binary.Left).Op);
    }

    [Fact]
    public void Parse_VectorMatching_OnIgnoringAndGrouping()
    {
        var on = Assert.IsType<PromBinary>(PromQlParser.Parse("a / on(job, instance) b")).Matching;
        Assert.True(on.On);
        Assert.Equal(["job", "instance"], on.Labels);
        Assert.Equal(PromCardinality.OneToOne, on.Cardinality);

        var left = Assert.IsType<PromBinary>(PromQlParser.Parse("a * ignoring(code) group_left(team) b")).Matching;
        Assert.False(left.On);
        Assert.Equal(["code"], left.Labels);
        Assert.Equal(PromCardinality.ManyToOne, left.Cardinality);
        Assert.Equal(["team"], left.Include);

        var right = Assert.IsType<PromBinary>(PromQlParser.Parse("a * on(job) group_right b")).Matching;
        Assert.Equal(PromCardinality.OneToMany, right.Cardinality);
    }

    [Fact]
    public void Parse_AggregationAfterBinaryOperator_StillTakesItsGroupingClause()
    {
        var binary = Assert.IsType<PromBinary>(PromQlParser.Parse("sum(a) by (job) / sum(b) by (job)"));

        Assert.Equal(["job"], Assert.IsType<PromAggregation>(binary.Left).Labels);
        Assert.Equal(["job"], Assert.IsType<PromAggregation>(binary.Right).Labels);
    }

    [Theory]
    [InlineData("x offset 5m", 300, false)]
    [InlineData("x{a=\"b\"} offset 1h", 3600, false)]
    [InlineData("rate(x[5m] offset 1d)", 86400, true)]
    public void Parse_Offset(string query, int seconds, bool rangeFunction)
    {
        var selector = PromQlParser.Parse(query) switch
        {
            PromRangeFunction fn when rangeFunction => fn.Selector,
            PromSelector s => s,
            var other => throw new Xunit.Sdk.XunitException($"unexpected {other}"),
        };

        Assert.Equal(TimeSpan.FromSeconds(seconds), selector.Offset);
    }

    [Theory]
    [InlineData("topk(3, rate(x[1m]))", "topk", 3.0)]
    [InlineData("bottomk by (job) (2, x)", "bottomk", 2.0)]
    [InlineData("quantile(0.9, x)", "quantile", 0.9)]
    [InlineData("quantile without (pod) (0.5, x)", "quantile", 0.5)]
    public void Parse_ParameterisedAggregation(string query, string op, double parameter)
    {
        var agg = Assert.IsType<PromAggregation>(PromQlParser.Parse(query));

        Assert.Equal(op, agg.Op);
        Assert.Equal(parameter, agg.Parameter);
    }

    [Theory]
    [InlineData("irate(x[5m])")]
    [InlineData("avg_over_time(x[5m])")]
    [InlineData("last_over_time(x[5m])")]
    public void Parse_RangeFunctions(string query) =>
        Assert.IsType<PromRangeFunction>(PromQlParser.Parse(query));

    [Theory]
    [InlineData("abs(x)", "abs", 0)]
    [InlineData("round(x)", "round", 0)]
    [InlineData("round(x, 0.5)", "round", 1)]
    [InlineData("clamp(x, 0, 1)", "clamp", 2)]
    [InlineData("clamp_min(x, 0)", "clamp_min", 1)]
    [InlineData("vector(1)", "vector", 0)]
    public void Parse_InstantFunctions(string query, string name, int parameters)
    {
        var fn = Assert.IsType<PromFunction>(PromQlParser.Parse(query));

        Assert.Equal(name, fn.Name);
        Assert.Equal(parameters, fn.Parameters.Count);
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("1h30m", 5400)]
    [InlineData("2d", 172800)]
    public void ParseDuration_Units(string text, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), PromQlParser.ParseDuration(text));
}
