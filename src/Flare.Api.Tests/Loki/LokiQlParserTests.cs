using Flare.Api.Loki;
using Xunit;

namespace Flare.Api.Tests.Loki;

public class LokiQlParserTests
{
    [Fact]
    public void Parse_SelectorWithMatchers()
    {
        var selector = Assert.IsType<LokiLogSelector>(LokiQlParser.Parse("""{service_name="api", level!="debug", k8s_pod_name=~"web-.*", env!~"dev|test"}"""));

        Assert.Equal(
            [(LokiMatchOp.Equal, "service_name"), (LokiMatchOp.NotEqual, "level"), (LokiMatchOp.Regex, "k8s_pod_name"), (LokiMatchOp.NotRegex, "env")],
            selector.Matchers.Select(m => (m.Op, m.Name)));
    }

    [Fact]
    public void Parse_LineFiltersAndAlternatives()
    {
        var selector = Assert.IsType<LokiLogSelector>(LokiQlParser.Parse("""{a="b"} |= "error" or "fatal" != `timeout` |~ "5\\d\\d" !~ "\d+" """));

        var filters = selector.Stages.Cast<LokiLineFilter>().ToList();
        Assert.Equal([LokiMatchOp.Equal, LokiMatchOp.NotEqual, LokiMatchOp.Regex, LokiMatchOp.NotRegex], filters.Select(f => f.Op));
        Assert.Equal(["error", "fatal"], filters[0].Patterns);
        Assert.Equal("timeout", filters[1].Patterns[0]);
        Assert.Equal(@"5\d\d", filters[2].Patterns[0]);
        // An unknown escape is kept as written so a regex such as \d+ works.
        Assert.Equal(@"\d+", filters[3].Patterns[0]);
    }

    [Fact]
    public void Parse_JsonAndLabelFilters()
    {
        var selector = Assert.IsType<LokiLogSelector>(LokiQlParser.Parse("""{a="b"} | json | status >= 500 and (level="error" or level="fatal") | drop __error__"""));

        Assert.IsType<LokiJsonStage>(selector.Stages[0]);
        var filter = Assert.IsType<LokiLabelFilterStage>(selector.Stages[1]);
        var and = Assert.IsType<LokiAnd>(filter.Predicate);
        var status = Assert.IsType<LokiComparison>(and.Left);
        Assert.Equal((LokiCompareOp.GreaterOrEqual, 500d), (status.Op, status.Number));
        Assert.IsType<LokiOr>(and.Right);
        Assert.IsType<LokiDropStage>(selector.Stages[2]);
    }

    [Fact]
    public void Parse_JsonExpressions()
    {
        var selector = Assert.IsType<LokiLogSelector>(LokiQlParser.Parse("""{a="b"} | json user="user.id", ua="request.agent" | user="42" """));

        var json = Assert.IsType<LokiJsonStage>(selector.Stages[0]);
        Assert.Equal([("user", "user.id"), ("ua", "request.agent")], json.Expressions);
    }

    [Fact]
    public void Parse_CommaIsAndInLabelFilter()
    {
        var selector = Assert.IsType<LokiLogSelector>(LokiQlParser.Parse("""{a="b"} | x="1", y="2" """));

        Assert.IsType<LokiAnd>(Assert.IsType<LokiLabelFilterStage>(selector.Stages[0]).Predicate);
    }

    [Theory]
    [InlineData("rate({a=\"b\"}[5m])", "rate", 300)]
    [InlineData("count_over_time({a=\"b\"} |= \"x\" [1h30m])", "count_over_time", 5400)]
    [InlineData("bytes_rate({a=\"b\"}[30s])", "bytes_rate", 30)]
    public void Parse_RangeAggregation(string query, string function, int seconds)
    {
        var range = Assert.IsType<LokiRangeAggregation>(LokiQlParser.Parse(query));

        Assert.Equal(function, range.Function);
        Assert.Equal(TimeSpan.FromSeconds(seconds), range.Range);
    }

    [Theory]
    [InlineData("sum by (level) (rate({a=\"b\"}[1m]))", false, new[] { "level" })]
    [InlineData("sum(rate({a=\"b\"}[1m])) by (level, service_name)", false, new[] { "level", "service_name" })]
    [InlineData("max without (pod) (rate({a=\"b\"}[1m]))", true, new[] { "pod" })]
    [InlineData("count(rate({a=\"b\"}[1m]))", false, new string[0])]
    public void Parse_VectorAggregation(string query, bool without, string[] labels)
    {
        var aggregation = Assert.IsType<LokiVectorAggregation>(LokiQlParser.Parse(query));

        Assert.Equal(without, aggregation.Without);
        Assert.Equal(labels, aggregation.Labels);
    }

    [Fact]
    public void Parse_TopK()
    {
        var topk = Assert.IsType<LokiVectorAggregation>(LokiQlParser.Parse("topk(3, sum by (service_name) (rate({a=\"b\"}[1m])))"));

        Assert.Equal(("topk", 3), (topk.Function, topk.K));
        Assert.IsType<LokiVectorAggregation>(topk.Inner);
    }

    [Fact]
    public void Parse_BinaryPrecedence()
    {
        var plus = Assert.IsType<LokiBinary>(LokiQlParser.Parse("vector(1) + vector(2) * 3"));

        Assert.Equal('+', plus.Op);
        Assert.Equal('*', Assert.IsType<LokiBinary>(plus.Right).Op);
    }

    [Fact]
    public void Parse_ErrorRatio()
    {
        var ratio = Assert.IsType<LokiBinary>(LokiQlParser.Parse("""sum(rate({a="b"} |= "error" [5m])) / sum(rate({a="b"}[5m]))"""));

        Assert.Equal('/', ratio.Op);
    }

    [Theory]
    [InlineData("""{a="b"} | logfmt""", "logfmt")]
    [InlineData("""{a="b"} | line_format "{{.x}}" """, "line_format")]
    [InlineData("""{a="b"} |> "<_> error" """, "pattern")]
    [InlineData("""sum_over_time({a="b"} | unwrap x [1m])""", "unwrap")]
    [InlineData("""rate({a="b"}[1m] offset 5m)""", "offset")]
    [InlineData("""{a="b"} | drop level""", "level")]
    [InlineData("""{}""", "at least one matcher")]
    [InlineData("""{a="b"} | latency > 5s""", "duration")]
    public void Parse_UnsupportedConstructsNameThemselves(string query, string mention)
    {
        var ex = Assert.Throws<LokiException>(() => LokiQlParser.Parse(query));

        Assert.Contains(mention, ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{a=\"b\"")]
    [InlineData("rate({a=\"b\"})")]
    [InlineData("{a=\"b\"} |= error")]
    [InlineData("nosuchfunction(1)")]
    public void Parse_MalformedQueriesThrow(string query) =>
        Assert.Throws<LokiException>(() => LokiQlParser.Parse(query));

    [Fact]
    public void ParseSelector_RejectsMetricQueries() =>
        Assert.Throws<LokiException>(() => LokiQlParser.ParseSelector("rate({a=\"b\"}[1m])"));

    [Theory]
    [InlineData("5m", 300)]
    [InlineData("1h30m", 5400)]
    [InlineData("500ms", 0.5)]
    public void ParseDuration_Units(string text, double seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), LokiQlParser.ParseDuration(text));
}
