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
    [InlineData("a / b", "between series")]
    [InlineData("rate(x[5m]) * 100", "between series")]
    [InlineData("topk(3, x)", "not supported")]
    [InlineData("x offset 5m", "offset")]
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
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("1h30m", 5400)]
    [InlineData("2d", 172800)]
    public void ParseDuration_Units(string text, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), PromQlParser.ParseDuration(text));
}
