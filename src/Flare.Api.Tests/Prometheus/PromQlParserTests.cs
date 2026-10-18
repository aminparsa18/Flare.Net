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
    [InlineData("quantile_over_time(0.5, x[5m])", "not supported")]
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
    [InlineData("x @ foo()", "start() or end()")]
    [InlineData("x @", "unix timestamp")]
    [InlineData("x @ 1 @ 2", "unexpected")]
    [InlineData("rate(x)", "range selector")]
    [InlineData("x[5m]", "range selector")]
    [InlineData("rate(x[5m:1m])", "over a subquery is not supported")]
    [InlineData("x[5m:1m]", "range selector or subquery")]
    [InlineData("x[5m][1h:1m]", "instant vector")]
    [InlineData("1[5m:1m]", "instant vector")]
    [InlineData("max_over_time(x[5m:1m]) + x[5m:1m]", "range selector")]
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
    [InlineData("min_over_time(x[5m])")]
    [InlineData("max_over_time(x[5m])")]
    [InlineData("sum_over_time(x[5m])")]
    [InlineData("count_over_time(x[5m])")]
    public void Parse_RangeFunctions(string query) =>
        Assert.IsType<PromRangeFunction>(PromQlParser.Parse(query));

    [Theory]
    [InlineData("x @ 1700000000", "Timestamp", 1_700_000_000_000)]
    [InlineData("x @ 1700000000.5", "Timestamp", 1_700_000_000_500)]
    [InlineData("x @ -30", "Timestamp", -30_000)]
    [InlineData("x @ start()", "Start", 0)]
    [InlineData("x @ end()", "End", 0)]
    [InlineData("x{a=\"b\"} @ 10", "Timestamp", 10_000)]
    public void Parse_AtModifier_OnSelector(string query, string kind, long timestampMs)
    {
        var at = Assert.IsType<PromSelector>(PromQlParser.Parse(query)).At;

        Assert.Equal(new PromAt(Enum.Parse<PromAtKind>(kind), timestampMs), at);
    }

    [Theory]
    [InlineData("x @ 100 offset 5m")]
    [InlineData("x offset 5m @ 100")]
    public void Parse_AtAndOffset_AcceptEitherOrder(string query)
    {
        var selector = Assert.IsType<PromSelector>(PromQlParser.Parse(query));

        Assert.Equal(TimeSpan.FromMinutes(5), selector.Offset);
        Assert.Equal(100_000, selector.At!.TimestampMs);
    }

    [Fact]
    public void Parse_AtModifier_OnRangeSelector()
    {
        var fn = Assert.IsType<PromRangeFunction>(PromQlParser.Parse("rate(x[5m] @ end())"));

        Assert.Equal(PromAtKind.End, fn.Selector.At!.Kind);
        Assert.Equal(TimeSpan.FromMinutes(5), fn.Selector.Range);
    }

    [Fact]
    public void Parse_Subquery_OverAFunction()
    {
        var fn = Assert.IsType<PromSubqueryFunction>(PromQlParser.Parse("max_over_time(rate(x[5m])[1h:1m])"));

        Assert.Equal("max_over_time", fn.Name);
        Assert.Equal(TimeSpan.FromHours(1), fn.Subquery.Range);
        Assert.Equal(TimeSpan.FromMinutes(1), fn.Subquery.Resolution);
        Assert.IsType<PromRangeFunction>(fn.Subquery.Inner);
    }

    [Fact]
    public void Parse_Subquery_DefaultResolutionIsNull_AndModifiersFollowTheBracket()
    {
        var fn = Assert.IsType<PromSubqueryFunction>(PromQlParser.Parse("avg_over_time((sum(x) / 2)[30m:] offset 1h @ 100)"));

        Assert.Null(fn.Subquery.Resolution);
        Assert.Equal(TimeSpan.FromHours(1), fn.Subquery.Offset);
        Assert.Equal(100_000, fn.Subquery.At!.TimestampMs);
        Assert.IsType<PromBinary>(fn.Subquery.Inner);
    }

    [Fact]
    public void Parse_Subquery_OverASelectorWithItsOwnOffset()
    {
        var fn = Assert.IsType<PromSubqueryFunction>(PromQlParser.Parse("min_over_time(x offset 5m[10m:1m])"));

        Assert.Equal(TimeSpan.FromMinutes(5), Assert.IsType<PromSelector>(fn.Subquery.Inner).Offset);
    }

    [Fact]
    public void Selectors_Collect_WalksIntoSubqueries()
    {
        var selectors = PromSelectors.Collect(PromQlParser.Parse("max_over_time(sum(x)[1h:1m]) + y"));

        Assert.Equal(["x", "y"], selectors.Select(s => s.MetricName));
    }

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

    [Fact]
    public void Parse_Absent_CarriesEqualityMatchersAsLabels()
    {
        var absent = Assert.IsType<PromAbsent>(PromQlParser.Parse("absent(up{job=\"api\", env!=\"dev\", zone=\"a\", zone=~\"b\"})"));

        Assert.Equal(new Dictionary<string, string> { ["job"] = "api" }, absent.Labels);
    }

    [Fact]
    public void Parse_LabelReplace_AndLabelJoin()
    {
        var replace = Assert.IsType<PromLabelFunction>(PromQlParser.Parse("label_replace(x, \"dst\", \"$1\", \"src\", \"(.*)\")"));
        var join = Assert.IsType<PromLabelFunction>(PromQlParser.Parse("label_join(x, \"dst\", \"-\", \"a\", \"b\")"));

        Assert.Equal(["dst", "$1", "src", "(.*)"], replace.Arguments);
        Assert.Equal(["dst", "-", "a", "b"], join.Arguments);
    }

    [Theory]
    [InlineData("label_replace(x, \"a\", \"b\")")]
    [InlineData("label_join(x, \"a\")")]
    [InlineData("label_replace(x, a, b, c, d)")]
    public void Parse_LabelFunctions_RejectBadArguments(string query) =>
        Assert.Throws<PromQlException>(() => PromQlParser.Parse(query));

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("1h30m", 5400)]
    [InlineData("2d", 172800)]
    public void ParseDuration_Units(string text, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), PromQlParser.ParseDuration(text));

    [Fact]
    public void Parse_Time_IsAScalar()
    {
        Assert.IsType<PromTime>(PromQlParser.Parse("time()"));
        Assert.True(PromExprs.IsScalar(PromQlParser.Parse("time() - 60")));
        Assert.True(PromExprs.IsScalar(PromQlParser.Parse("scalar(sum(x)) * 2")));
        Assert.False(PromExprs.IsScalar(PromQlParser.Parse("x * 2")));
    }

    [Fact]
    public void Parse_TimeInArithmetic_IsNotFolded_ButConstantsStillAre()
    {
        Assert.IsType<PromBinary>(PromQlParser.Parse("time() - 60"));
        Assert.Equal(7, Assert.IsType<PromNumber>(PromQlParser.Parse("3 + 4")).Value);
    }

    [Fact]
    public void Parse_VectorOfAScalarExpression()
    {
        var fn = Assert.IsType<PromFunction>(PromQlParser.Parse("vector(time() - 5)"));

        Assert.Equal("vector", fn.Name);
        Assert.IsType<PromBinary>(fn.Argument);
    }

    [Theory]
    [InlineData("sum(time())")]
    [InlineData("abs(time())")]
    [InlineData("scalar(time())")]
    [InlineData("sort(time())")]
    [InlineData("time() > 5")]
    [InlineData("x and time()")]
    public void Parse_ScalarWhereAVectorIsExpected_IsRejected(string query) =>
        Assert.Throws<PromQlException>(() => PromQlParser.Parse(query));

    [Fact]
    public void Parse_Scalar()
    {
        var expr = Assert.IsType<PromScalar>(PromQlParser.Parse("scalar(sum(x))"));

        Assert.IsType<PromAggregation>(expr.Inner);
    }

    [Theory]
    [InlineData("delta")]
    [InlineData("changes")]
    [InlineData("resets")]
    [InlineData("deriv")]
    public void Parse_GaugeRangeFunctions(string function)
    {
        var fn = Assert.IsType<PromRangeFunction>(PromQlParser.Parse($"{function}(temp[5m])"));

        Assert.Equal(function, fn.Name);
        Assert.Null(fn.Parameter);
        Assert.IsType<PromSubqueryFunction>(PromQlParser.Parse($"{function}(temp[1h:1m])"));
    }

    [Fact]
    public void Parse_PredictLinear_TakesSecondsAhead()
    {
        var fn = Assert.IsType<PromRangeFunction>(PromQlParser.Parse("predict_linear(temp[1h], 4 * 3600)"));

        Assert.Equal(14400, fn.Parameter);
        Assert.Throws<PromQlException>(() => PromQlParser.Parse("predict_linear(temp[1h])"));
        Assert.Throws<PromQlException>(() => PromQlParser.Parse("predict_linear(temp[1h], x)"));
    }

    [Fact]
    public void Parse_RateOverASubquery_StaysRejected() =>
        Assert.Throws<PromQlException>(() => PromQlParser.Parse("rate(x[1h:1m])"));

    [Theory]
    [InlineData("changes")]
    [InlineData("time")]
    [InlineData("scalar")]
    [InlineData("sort")]
    [InlineData("deriv")]
    public void Parse_AMetricMayShareAFunctionsName(string name)
    {
        var selector = Assert.IsType<PromSelector>(PromQlParser.Parse($"{name}{{job=\"a\"}}"));

        Assert.Equal(name, selector.MetricName);
        Assert.Equal(name, Assert.IsType<PromSelector>(PromQlParser.Parse(name)).MetricName);
    }

    [Theory]
    [InlineData("sort(x)", "sort", 0)]
    [InlineData("sort_desc(x)", "sort_desc", 0)]
    [InlineData("""sort_by_label(x, "a", "b")""", "sort_by_label", 2)]
    [InlineData("""sort_by_label_desc(x, "a")""", "sort_by_label_desc", 1)]
    public void Parse_Sort(string query, string name, int labels)
    {
        var sort = Assert.IsType<PromSort>(PromQlParser.Parse(query));

        Assert.Equal(name, sort.Name);
        Assert.Equal(labels, sort.Labels.Count);
    }

    [Theory]
    [InlineData("sort(x, \"a\")")]
    [InlineData("sort_by_label(x)")]
    public void Parse_SortArgumentErrors(string query) =>
        Assert.Throws<PromQlException>(() => PromQlParser.Parse(query));

    [Fact]
    public void Parse_CountValues()
    {
        var plain = Assert.IsType<PromAggregation>(PromQlParser.Parse("""count_values("version", build_info)"""));
        var grouped = Assert.IsType<PromAggregation>(PromQlParser.Parse("""count_values by (job) ("version", build_info)"""));

        Assert.Equal("version", plain.ValueLabel);
        Assert.Equal(["job"], grouped.Labels);
        Assert.Throws<PromQlException>(() => PromQlParser.Parse("count_values(1, x)"));
        Assert.Throws<PromQlException>(() => PromQlParser.Parse("""count_values("bad-label", x)"""));
    }

    [Fact]
    public void SelectorCollector_FindsSelectorsInsideScalarAndSort() =>
        Assert.Equal(["a", "b"], PromSelectors.Collect(PromQlParser.Parse("sort(a) * scalar(b)")).Select(s => s.MetricName));

    [Fact]
    public void Parse_IdeltaAndHoltWinters()
    {
        Assert.Equal("idelta", Assert.IsType<PromRangeFunction>(PromQlParser.Parse("idelta(temp[5m])")).Name);
        Assert.IsType<PromSubqueryFunction>(PromQlParser.Parse("idelta(temp[1h:1m])"));

        var hw = Assert.IsType<PromRangeFunction>(PromQlParser.Parse("holt_winters(temp[5m], 0.3, 0.1)"));
        Assert.Equal((0.3, 0.1), (hw.Parameter, hw.Parameter2));
        // The Prometheus 3 name is an alias.
        Assert.Equal("holt_winters", Assert.IsType<PromRangeFunction>(PromQlParser.Parse("double_exponential_smoothing(temp[5m], 0.3, 0.1)")).Name);
    }

    [Theory]
    [InlineData("holt_winters(temp[5m], 0, 0.5)")]
    [InlineData("holt_winters(temp[5m], 0.5, 1)")]
    [InlineData("holt_winters(temp[5m], 0.5)")]
    [InlineData("holt_winters(temp[5m], x, 0.5)")]
    public void Parse_HoltWintersFactors_AreValidated(string query) =>
        Assert.Throws<PromQlException>(() => PromQlParser.Parse(query));

    [Fact]
    public void Parse_Timestamp()
    {
        Assert.IsType<PromSelector>(Assert.IsType<PromTimestamp>(PromQlParser.Parse("timestamp(temp)")).Inner);
        Assert.Throws<PromQlException>(() => PromQlParser.Parse("timestamp(time())"));
        Assert.IsType<PromSelector>(PromQlParser.Parse("timestamp"));
    }

    [Fact]
    public void Parse_HistogramFraction()
    {
        var hf = Assert.IsType<PromHistogramFraction>(PromQlParser.Parse("histogram_fraction(0, 0.3, rate(d_bucket[5m]))"));

        Assert.Equal((0.0, 0.3), (hf.Lower, hf.Upper));
        Assert.Equal(double.PositiveInfinity, Assert.IsType<PromHistogramFraction>(PromQlParser.Parse("histogram_fraction(0.3, +Inf, rate(d_bucket[5m]))")).Upper);
        Assert.Throws<PromQlException>(() => PromQlParser.Parse("histogram_fraction(x, 1, rate(d_bucket[5m]))"));
    }

    [Fact]
    public void Parse_NamePattern_IsAMultiMetricSelector()
    {
        var pattern = Assert.IsType<PromSelector>(PromQlParser.Parse("""{__name__=~"http_.*", job="api"}"""));
        var single = Assert.IsType<PromSelector>(PromQlParser.Parse("""{__name__="up"}"""));

        Assert.True(pattern.IsMultiMetric);
        Assert.Single(pattern.NameMatchers);
        Assert.Equal(["job"], pattern.LabelMatchers.Select(m => m.Name));
        Assert.False(single.IsMultiMetric);
        Assert.IsType<PromRangeFunction>(PromQlParser.Parse("""rate({__name__=~"jobs_.*"}[5m])"""));
        Assert.Throws<PromQlException>(() => PromQlParser.Parse("""{__name__!~"http_.*"}"""));
    }
}
