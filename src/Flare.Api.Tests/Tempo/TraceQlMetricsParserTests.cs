using Flare.Api.Tempo;
using Xunit;

namespace Flare.Api.Tests.Tempo;

public class TraceQlMetricsParserTests
{
    [Fact]
    public void Rate_WithoutGrouping()
    {
        var query = TraceQlParser.ParseMetrics("""{ resource.service.name = "api" && status = error } | rate()""");

        Assert.Equal(TraceQlMetricsFunction.Rate, query.Function);
        Assert.Equal("rate", query.FunctionName);
        Assert.Equal(2, query.Spanset.Predicates.Count);
        Assert.Empty(query.By);
        Assert.Empty(query.Quantiles);
    }

    [Theory]
    [InlineData("count_over_time()", TraceQlMetricsFunction.CountOverTime)]
    [InlineData("avg_over_time(duration)", TraceQlMetricsFunction.AvgOverTime)]
    [InlineData("min_over_time(duration)", TraceQlMetricsFunction.MinOverTime)]
    [InlineData("max_over_time(duration)", TraceQlMetricsFunction.MaxOverTime)]
    [InlineData("sum_over_time(duration)", TraceQlMetricsFunction.SumOverTime)]
    public void EachFunction_IsRecognised(string stage, TraceQlMetricsFunction expected)
    {
        var query = TraceQlParser.ParseMetrics("{ } | " + stage);

        Assert.Equal(expected, query.Function);
        Assert.StartsWith(query.FunctionName, stage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("avg_over_time(span.size)", TraceQlMetricsFunction.AvgOverTime, "span.size")]
    [InlineData("min_over_time(resource.k8s.pod.cpu)", TraceQlMetricsFunction.MinOverTime, "resource.k8s.pod.cpu")]
    [InlineData("max_over_time(.http.response.body.size)", TraceQlMetricsFunction.MaxOverTime, ".http.response.body.size")]
    [InlineData("sum_over_time(span.bytes)", TraceQlMetricsFunction.SumOverTime, "span.bytes")]
    [InlineData("quantile_over_time(span.size, .5, .9)", TraceQlMetricsFunction.QuantileOverTime, "span.size")]
    public void ValueFunctions_TakeANumericAttribute(string stage, TraceQlMetricsFunction expected, string field)
    {
        var query = TraceQlParser.ParseMetrics("{ } | " + stage + " by (name)");

        Assert.Equal(expected, query.Function);
        Assert.Equal(field, query.ValueField);
        Assert.Equal(new[] { "name" }, query.By);
    }

    [Fact]
    public void Duration_IsTheDefaultValue()
    {
        Assert.Null(TraceQlParser.ParseMetrics("{ } | avg_over_time(duration)").ValueField);
        Assert.Null(TraceQlParser.ParseMetrics("{ } | rate()").ValueField);
    }

    [Fact]
    public void HistogramOverTime_TakesDurationOrAnAttribute()
    {
        var duration = TraceQlParser.ParseMetrics("{ } | histogram_over_time(duration) by (resource.service.name)");
        var attribute = TraceQlParser.ParseMetrics("{ } | histogram_over_time(span.size)");

        Assert.Equal(TraceQlMetricsFunction.HistogramOverTime, duration.Function);
        Assert.Equal("histogram_over_time", duration.FunctionName);
        Assert.Null(duration.ValueField);
        Assert.Equal("span.size", attribute.ValueField);
    }

    [Theory]
    [InlineData("| topk(5)", true, 5)]
    [InlineData("| bottomk(1)", false, 1)]
    [InlineData("| topk(10) with(exemplars=true)", true, 10)]
    public void RankStage_IsReadAfterBy(string stage, bool top, int k)
    {
        var query = TraceQlParser.ParseMetrics("{ } | rate() by (name) " + stage);

        Assert.Equal(new TraceQlRank(top, k), Assert.Single(query.Stages));
        Assert.Equal(new[] { "name" }, query.By);
    }

    [Fact]
    public void NoStages_ByDefault() => Assert.Empty(TraceQlParser.ParseMetrics("{ } | rate()").Stages);

    [Theory]
    [InlineData("> 5", TraceQlComparison.Greater, 5)]
    [InlineData(">= 0.5", TraceQlComparison.GreaterOrEqual, 0.5)]
    [InlineData("< .25", TraceQlComparison.Less, 0.25)]
    [InlineData("<= 3", TraceQlComparison.LessOrEqual, 3)]
    [InlineData("= 0", TraceQlComparison.Equal, 0)]
    [InlineData("!= -1", TraceQlComparison.NotEqual, -1)]
    [InlineData(">5", TraceQlComparison.Greater, 5)]
    public void Comparison_IsReadAfterTheFunctionAndBy(string stage, TraceQlComparison op, double value)
    {
        var query = TraceQlParser.ParseMetrics("{ } | rate() by (name) " + stage);

        Assert.Equal(new TraceQlFilter(op, value), Assert.Single(query.Stages));
        Assert.Equal(new[] { "name" }, query.By);
    }

    [Fact]
    public void Stages_KeepTheOrderTheyAreWritten()
    {
        var comparisonFirst = TraceQlParser.ParseMetrics("{ } | rate() by (name) > 0 | topk(5)");
        var rankFirst = TraceQlParser.ParseMetrics("{ } | rate() by (name) | topk(5) > 10 with(exemplars=true)");
        var chain = TraceQlParser.ParseMetrics("{ } | rate() | topk(3) | bottomk(1) < 2 > 1");

        Assert.Equal(new TraceQlMetricsStage[] { new TraceQlFilter(TraceQlComparison.Greater, 0), new TraceQlRank(true, 5) }, comparisonFirst.Stages);
        Assert.Equal(new TraceQlMetricsStage[] { new TraceQlRank(true, 5), new TraceQlFilter(TraceQlComparison.Greater, 10) }, rankFirst.Stages);
        Assert.True(rankFirst.Exemplars);
        Assert.Equal(
            new TraceQlMetricsStage[] { new TraceQlRank(true, 3), new TraceQlRank(false, 1), new TraceQlFilter(TraceQlComparison.Less, 2), new TraceQlFilter(TraceQlComparison.Greater, 1) },
            chain.Stages);
    }

    [Theory]
    [InlineData(">", "ends unexpectedly")]
    [InlineData("> 5s", "not a number")]
    [InlineData("> name", "not a number")]
    [InlineData("=~ 5", "unexpected")]
    [InlineData("> NaN", "not a number")]
    public void Comparison_NeedsANumber(string stage, string message)
    {
        var ex = Assert.Throws<TraceQlException>(() => TraceQlParser.ParseMetrics("{ } | rate() " + stage));

        Assert.Contains(message, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TooManyStages_AreRejected()
    {
        var query = "{ } | rate()" + string.Concat(Enumerable.Repeat(" > 1", TraceQlParser.MaxStages + 1));

        var ex = Assert.Throws<TraceQlException>(() => TraceQlParser.ParseMetrics(query));

        Assert.Contains("at most 10 stages", ex.Message);
    }

    [Theory]
    [InlineData("with(exemplars=true)", true)]
    [InlineData("with(exemplars=false)", false)]
    [InlineData("", false)]
    public void ExemplarsHint_IsReported(string hint, bool expected) =>
        Assert.Equal(expected, TraceQlParser.ParseMetrics("{ } | rate() " + hint).Exemplars);

    [Fact]
    public void QuantileOverTime_ReadsTheQuantiles()
    {
        var query = TraceQlParser.ParseMetrics("{ } | quantile_over_time(duration, .5, 0.95, 1)");

        Assert.Equal(TraceQlMetricsFunction.QuantileOverTime, query.Function);
        Assert.Equal(new[] { 0.5, 0.95, 1 }, query.Quantiles);
    }

    [Fact]
    public void By_ReadsEveryFieldInOrder()
    {
        var query = TraceQlParser.ParseMetrics("{ } | rate() by (resource.service.name, span.http.route, name, status, .k8s.namespace.name)");

        Assert.Equal(new[] { "resource.service.name", "span.http.route", "name", "status", ".k8s.namespace.name" }, query.By);
    }

    [Fact]
    public void ExemplarsHint_IsAcceptedAndIgnored()
    {
        var query = TraceQlParser.ParseMetrics("{ } | rate() by (name) with(exemplars=true)");

        Assert.Equal(new[] { "name" }, query.By);
    }

    [Theory]
    [InlineData("{ } | compare({ status = error })", "compare")]
    [InlineData("{ } | rate() | filter(true)", "'filter' is not supported")]
    [InlineData("{ } | rate() | topk(0)", "whole number")]
    [InlineData("{ } | rate() | topk(x)", "whole number")]
    [InlineData("{ } | count() > 2", "'count'")]
    [InlineData("{ } | quantile_over_time(duration)", "1 to 10 quantiles")]
    [InlineData("{ } | quantile_over_time(duration, 1.5)", "not a quantile")]
    [InlineData("{ } | quantile_over_time(name, .5)", "quantile_over_time(name) is not supported")]
    [InlineData("{ } | avg_over_time(status)", "avg_over_time(status) is not supported")]
    [InlineData("{ } | avg_over_time(span.)", "numeric attribute")]
    [InlineData("{ } | avg_over_time()", "needs the field")]
    [InlineData("{ } | rate() by (duration)", "cannot be grouped by")]
    [InlineData("{ } | rate() by (span.)", "cannot be grouped by")]
    [InlineData("{ } | rate() by (name, name, name, name, name, name)", "at most 5")]
    [InlineData("{ } | rate() with(sample=true)", "hint")]
    [InlineData("{ }", "needs a function")]
    [InlineData("{ } | rate() extra", "unexpected")]
    [InlineData("{ a = \"1\" } && { b = \"2\" } | rate()", "one spanset")]
    [InlineData("{ a = \"1\" } || { b = \"2\" } | rate()", "one spanset")]
    [InlineData("{ a = \"1\" } >> { b = \"2\" } && { c = \"3\" } | rate()", "one spanset")]
    [InlineData("| rate()", "pipeline")]
    [InlineData("", "empty")]
    public void Unsupported_IsRejectedNamingTheConstruct(string traceQl, string mentions)
    {
        var ex = Assert.Throws<TraceQlException>(() => TraceQlParser.ParseMetrics(traceQl));

        Assert.Contains(mentions, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{ name = \"a\" } >> { name = \"b\" } | rate()", false)]
    [InlineData("{ name = \"a\" } > { name = \"b\" } | rate() by (name)", true)]
    [InlineData("{ name = \"a\" } >> { name = \"b\" } | quantile_over_time(duration, .9) > 1 | topk(2)", false)]
    public void StructuralRelation_CountsTheRightHandSpanset(string traceQl, bool direct)
    {
        var query = TraceQlParser.ParseMetrics(traceQl);

        Assert.Equal("a", Assert.Single(query.Ancestor!.Predicates).Value.Text);
        Assert.Equal("b", Assert.Single(query.Spanset.Predicates).Value.Text);
        Assert.Equal(direct, query.AncestorDirect);
    }

    [Fact]
    public void APlainSpanset_HasNoAncestor() => Assert.Null(TraceQlParser.ParseMetrics("{ name = \"a\" } | rate()").Ancestor);

    [Fact]
    public void Search_StillRejectsAPipeline()
    {
        Assert.Throws<TraceQlException>(() => TraceQlParser.Parse("{ } | rate()"));
    }
}
