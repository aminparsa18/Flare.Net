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
    [InlineData("""{a="b"} | label_format x=y""", "label_format")]
    [InlineData("""{a="b"} | regexp "(?P<x>.+)" """, "regexp")]
    [InlineData("""rate_counter({a="b"} [1m])""", "needs '| unwrap")]
    [InlineData("""absent_over_time({a="b"} | unwrap x [1m])""", "unwrapped")]
    [InlineData("""{a="b"} | logfmt --lenient""", "--lenient")]
    [InlineData("""{a="b"} | logfmt --strict | logfmt""", "same --strict setting")]
    [InlineData("""{a="b"} | line_format "{{ if .x }}y{{ end }}" """, "'if' is not supported")]
    [InlineData("""{a="b"} | line_format "{{ .x | nosuchfn }}" """, "nosuchfn")]
    [InlineData("""{a="b"} | line_format "{{.x}}" |= "y" """, "after line_format")]
    [InlineData("""{a="b"} | line_format "{{.x}}" | json""", "after line_format")]
    [InlineData("""{a="b"} | line_format "{{.x}}" | line_format "{{.y}}" """, "only one line_format")]
    [InlineData("""{a="b"} | unwrap x""", "range function")]
    [InlineData("""sum_over_time({a="b"} [1m])""", "needs '| unwrap")]
    [InlineData("""rate({a="b"} | unwrap x [1m])""", "unwrapped")]
    [InlineData("""sum_over_time({a="b"} | unwrap x |= "y" [1m])""", "only label filters")]
    [InlineData("""sum_over_time({a="b"} | unwrap nosuch(x) [1m])""", "nosuch")]
    [InlineData("""quantile_over_time(2, {a="b"} | unwrap x [1m])""", "between 0 and 1")]
    [InlineData("""{a="b"} |> "<name> error" """, "named captures")]
    [InlineData("""{a="b"} |> "<_><_>" """, "two captures")]
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

    [Fact]
    public void Parse_Logfmt_BareAndWithExpressions()
    {
        var bare = Assert.IsType<LokiLogSelector>(LokiQlParser.Parse("""{a="b"} | logfmt"""));
        Assert.Empty(Assert.IsType<LokiLogfmtStage>(Assert.Single(bare.Stages)).Expressions);

        var mapped = Assert.IsType<LokiLogSelector>(LokiQlParser.Parse("""{a="b"} | logfmt status_code="status", user="user.id" | status_code >= 500"""));
        var stage = Assert.IsType<LokiLogfmtStage>(mapped.Stages[0]);
        Assert.Equal([("status_code", "status"), ("user", "user.id")], stage.Expressions);
        Assert.IsType<LokiLabelFilterStage>(mapped.Stages[1]);
    }

    [Fact]
    public void Parse_DrilldownMixedParserPipeline()
    {
        // What Logs Drilldown sends for a field whose parser is "mixed".
        var selector = Assert.IsType<LokiLogSelector>(LokiQlParser.Parse(
            """{service_name=~".+"} | detected_level=~"error|warn" |> "<_> failed" | json status="status" | json | logfmt | drop __error__, __error_details__ | status!="" | line_format "{{.msg}}" """));

        Assert.Collection(
            selector.Stages,
            s => Assert.IsType<LokiLabelFilterStage>(s),
            s => Assert.False(Assert.IsType<LokiPatternFilter>(s).Negated),
            s => Assert.Equal("status", Assert.Single(Assert.IsType<LokiJsonStage>(s).Expressions).Label),
            s => Assert.Empty(Assert.IsType<LokiJsonStage>(s).Expressions),
            s => Assert.IsType<LokiLogfmtStage>(s),
            s => Assert.IsType<LokiDropStage>(s),
            s => Assert.IsType<LokiLabelFilterStage>(s),
            s => Assert.IsType<LokiLineFormatStage>(s));
    }

    [Fact]
    public void Parse_PatternFilters()
    {
        var selector = Assert.IsType<LokiLogSelector>(LokiQlParser.Parse("""{a="b"} |> "<_> took <_>ms" !> "<_> health <_>" """));

        Assert.Equal(
            [(false, "<_> took <_>ms"), (true, "<_> health <_>")],
            selector.Stages.Cast<LokiPatternFilter>().Select(p => (p.Negated, p.Pattern)));
    }

    [Theory]
    [InlineData("sum_over_time", null)]
    [InlineData("avg_over_time", null)]
    [InlineData("min_over_time", null)]
    [InlineData("max_over_time", null)]
    [InlineData("first_over_time", null)]
    [InlineData("last_over_time", null)]
    [InlineData("stddev_over_time", null)]
    [InlineData("stdvar_over_time", null)]
    [InlineData("quantile_over_time", 0.99)]
    public void Parse_UnwrappedRangeFunctions(string function, double? quantile)
    {
        var args = quantile is { } q ? $"{q}, " : string.Empty;

        var range = Assert.IsType<LokiRangeAggregation>(LokiQlParser.Parse($$"""{{function}}({{args}}{a="b"} | json | unwrap latency [5m]) by (service_name)"""));

        Assert.Equal(function, range.Function);
        Assert.Equal(quantile, range.Quantile);
        Assert.True(range.IsUnwrapped);
        Assert.Equal(TimeSpan.FromMinutes(5), range.Range);
        Assert.False(range.Grouping!.Without);
        Assert.Equal(["service_name"], range.Grouping.Labels);
        var unwrap = range.Selector.Stages.OfType<LokiUnwrapStage>().Single();
        Assert.Equal(("latency", LokiUnwrapConversion.None), (unwrap.Label, unwrap.Conversion));
    }

    [Theory]
    [InlineData("duration(latency)", "Duration")]
    [InlineData("duration_seconds(latency)", "Duration")]
    [InlineData("bytes(latency)", "Bytes")]
    public void Parse_UnwrapConversions(string unwrap, string expected)
    {
        var range = Assert.IsType<LokiRangeAggregation>(LokiQlParser.Parse($$"""avg_over_time({a="b"} | unwrap {{unwrap}} | __error__="" [1m])"""));

        var stage = range.Selector.Stages.OfType<LokiUnwrapStage>().Single();
        Assert.Equal(("latency", Enum.Parse<LokiUnwrapConversion>(expected)), (stage.Label, stage.Conversion));
        Assert.IsType<LokiLabelFilterStage>(range.Selector.Stages[^1]);
    }

    [Fact]
    public void Parse_UnwrappedFunctionInsideVectorAggregation_HasNoGroupingOfItsOwn()
    {
        var sum = Assert.IsType<LokiVectorAggregation>(LokiQlParser.Parse("""sum by (service_name) (sum_over_time({a="b"} | unwrap x [1m]))"""));

        var range = Assert.IsType<LokiRangeAggregation>(sum.Inner);
        Assert.Null(range.Grouping);
    }

    // ---- ADR-0238 ----

    [Fact]
    public void Parse_RateCounterIsAnUnwrappedFunctionWithItsOwnGrouping()
    {
        var range = Assert.IsType<LokiRangeAggregation>(LokiQlParser.Parse("""rate_counter({a="b"} | unwrap bytes_total [5m]) by (service_name)"""));

        Assert.Equal("rate_counter", range.Function);
        Assert.True(range.IsUnwrapped);
        Assert.Equal(["service_name"], range.Grouping!.Labels);
    }

    [Fact]
    public void Parse_AbsentOverTimeTakesAPipelineAndARange()
    {
        var range = Assert.IsType<LokiRangeAggregation>(LokiQlParser.Parse("""absent_over_time({a="b"} |= "heartbeat" [5m])"""));

        Assert.Equal("absent_over_time", range.Function);
        Assert.False(range.IsUnwrapped);
        Assert.Equal(TimeSpan.FromMinutes(5), range.Range);
        Assert.IsType<LokiLineFilter>(Assert.Single(range.Selector.Stages));
    }

    [Theory]
    [InlineData("| logfmt", false, false)]
    [InlineData("| logfmt --strict", true, false)]
    [InlineData("| logfmt --keep-empty", false, true)]
    [InlineData("| logfmt --keep-empty --strict", true, true)]
    public void Parse_LogfmtFlags(string stage, bool strict, bool keepEmpty)
    {
        var selector = LokiQlParser.ParseSelector($$"""{a="b"} {{stage}}""");

        var logfmt = Assert.IsType<LokiLogfmtStage>(Assert.Single(selector.Stages));
        Assert.Equal((strict, keepEmpty), (logfmt.Strict, logfmt.KeepEmpty));
    }

    [Fact]
    public void Parse_LogfmtFlagsMayPrecedeExpressions()
    {
        var selector = LokiQlParser.ParseSelector("""{a="b"} | logfmt --strict code="status" """);

        var logfmt = Assert.IsType<LokiLogfmtStage>(Assert.Single(selector.Stages));
        Assert.True(logfmt.Strict);
        Assert.Equal([("code", "status")], logfmt.Expressions);
    }

    [Theory]
    [InlineData("""| drop __error__""", true)]
    [InlineData("""| drop __error__, __error_details__""", true)]
    [InlineData("""| drop __error_details__""", false)]
    public void Parse_DropRemembersWhetherItResetsTheError(string stage, bool resets)
    {
        var selector = LokiQlParser.ParseSelector($$"""{a="b"} {{stage}}""");

        Assert.Equal(resets, Assert.IsType<LokiDropStage>(Assert.Single(selector.Stages)).ResetsError);
    }

    [Theory]
    [InlineData("| __error__=\"\"", false)]
    [InlineData("| __error__!=\"\"", true)]
    [InlineData("| __error__=\"SampleExtractionErr\"", true)]
    [InlineData("| __error__!=\"JSONParserErr\"", true)]
    [InlineData("| __error__=~\".+\"", true)]
    [InlineData("| __error__!~\".+\"", false)]
    [InlineData("""| __error_details__!="" """, false)]
    [InlineData("""| status >= 500""", false)]
    [InlineData("""| status >= 500 or __error__!="" """, true)]
    public void KeepsErroredLines_FollowsWhatTheErrorFilterAccepts(string stage, bool keeps)
    {
        var range = Assert.IsType<LokiRangeAggregation>(LokiQlParser.Parse($$"""sum_over_time({a="b"} | unwrap x {{stage}} [1m])"""));

        Assert.Equal(keeps, range.KeepsErroredLines);
    }

    [Fact]
    public void KeepsErroredLines_CountsAFilterBeforeTheUnwrapToo()
    {
        var range = Assert.IsType<LokiRangeAggregation>(LokiQlParser.Parse("""sum_over_time({a="b"} | __error__!="JSONParserErr" | unwrap x [1m])"""));

        Assert.True(range.KeepsErroredLines);
    }
}
