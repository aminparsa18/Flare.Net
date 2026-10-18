using Flare.Ingest.Prometheus;
using Xunit;

namespace Flare.Ingest.Tests.Prometheus;

public class PrometheusExpositionParserTests
{
    [Fact]
    public void Parse_ReturnsEmpty_ForEmptyInput()
    {
        var result = PrometheusExpositionParser.Parse("");

        Assert.Empty(result.Samples);
        Assert.Empty(result.Types);
        Assert.Empty(result.HelpText);
    }

    [Fact]
    public void Parse_ParsesGaugeWithNoLabels()
    {
        var text = "# HELP up 1 if the target is up\n# TYPE up gauge\nup 1\n";

        var result = PrometheusExpositionParser.Parse(text);

        var sample = Assert.Single(result.Samples);
        Assert.Equal("up", sample.Name);
        Assert.Empty(sample.Labels);
        Assert.Equal(1d, sample.Value);
        Assert.Null(sample.TimestampMillis);
        Assert.Equal(PrometheusMetricType.Gauge, result.Types["up"]);
        Assert.Equal("1 if the target is up", result.HelpText["up"]);
    }

    [Fact]
    public void Parse_ParsesCounterWithLabelsAndExplicitTimestamp()
    {
        var text = "# TYPE http_requests_total counter\n" +
                    "http_requests_total{method=\"GET\",code=\"200\"} 1027 1700000000000\n";

        var result = PrometheusExpositionParser.Parse(text);

        var sample = Assert.Single(result.Samples);
        Assert.Equal("http_requests_total", sample.Name);
        Assert.Equal("GET", sample.Labels["method"]);
        Assert.Equal("200", sample.Labels["code"]);
        Assert.Equal(1027d, sample.Value);
        Assert.Equal(1_700_000_000_000L, sample.TimestampMillis);
        Assert.Equal(PrometheusMetricType.Counter, result.Types["http_requests_total"]);
    }

    [Fact]
    public void Parse_TreatsUndeclaredMetric_AsUntyped()
    {
        var result = PrometheusExpositionParser.Parse("mystery_metric 5\n");

        Assert.False(result.Types.ContainsKey("mystery_metric"));
        Assert.Equal(5d, Assert.Single(result.Samples).Value);
    }

    [Fact]
    public void Parse_UnescapesQuotedLabelValues()
    {
        var text = "m{a=\"quote\\\"here\",b=\"back\\\\slash\",c=\"line\\nbreak\"} 1\n";

        var sample = Assert.Single(PrometheusExpositionParser.Parse(text).Samples);

        Assert.Equal("quote\"here", sample.Labels["a"]);
        Assert.Equal("back\\slash", sample.Labels["b"]);
        Assert.Equal("line\nbreak", sample.Labels["c"]);
    }

    [Fact]
    public void Parse_ParsesSpecialFloatValues()
    {
        var text = "a +Inf\nb -Inf\nc NaN\n";

        var result = PrometheusExpositionParser.Parse(text);

        Assert.Equal(double.PositiveInfinity, result.Samples[0].Value);
        Assert.Equal(double.NegativeInfinity, result.Samples[1].Value);
        Assert.True(double.IsNaN(result.Samples[2].Value));
    }

    [Fact]
    public void Parse_SkipsBlankLinesAndPlainComments()
    {
        var text = "\n# just a comment, not HELP/TYPE\n\nmetric 1\n   \n";

        var result = PrometheusExpositionParser.Parse(text);

        Assert.Single(result.Samples);
    }

    [Fact]
    public void Parse_SkipsMalformedSampleLine_WithoutThrowing()
    {
        var text = "this is not a valid line===\nvalid_metric 42\n";

        var result = PrometheusExpositionParser.Parse(text);

        var sample = Assert.Single(result.Samples);
        Assert.Equal("valid_metric", sample.Name);
        Assert.Equal(42d, sample.Value);
    }

    [Fact]
    public void Parse_HandlesHistogramBucketFamily()
    {
        var text =
            "# TYPE request_duration_seconds histogram\n" +
            "request_duration_seconds_bucket{le=\"0.1\"} 3\n" +
            "request_duration_seconds_bucket{le=\"0.5\"} 7\n" +
            "request_duration_seconds_bucket{le=\"1\"} 10\n" +
            "request_duration_seconds_bucket{le=\"+Inf\"} 12\n" +
            "request_duration_seconds_sum 8.2\n" +
            "request_duration_seconds_count 12\n";

        var result = PrometheusExpositionParser.Parse(text);

        Assert.Equal(PrometheusMetricType.Histogram, result.Types["request_duration_seconds"]);
        Assert.Equal(6, result.Samples.Count);
        Assert.All(
            result.Samples.Where(s => s.Name.EndsWith("_bucket", StringComparison.Ordinal)),
            s => Assert.True(s.Labels.ContainsKey("le")));
    }

    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public void Parse_KeepsSampleAndExemplar_WhenLineHasExemplarSuffix()
    {
        var text = "# TYPE latency histogram\n" +
                   $"latency_bucket{{le=\"0.5\"}} 7 # {{trace_id=\"{TraceId}\",span_id=\"00f067aa0ba902b7\",region=\"eu\"}} 0.43 1700000000.25\n";

        var sample = Assert.Single(PrometheusExpositionParser.Parse(text, openMetrics: true).Samples);

        Assert.Equal(7d, sample.Value);
        var exemplar = Assert.IsType<PrometheusExemplar>(sample.Exemplar);
        Assert.Equal(0.43d, exemplar.Value);
        Assert.Equal(1_700_000_000_250L, exemplar.TimestampMillis);
        Assert.Equal(TraceId, exemplar.Labels["trace_id"]);
        Assert.Equal("eu", exemplar.Labels["region"]);
    }

    [Fact]
    public void Parse_KeepsSampleWithoutExemplar_WhenExemplarIsMalformed()
    {
        var sample = Assert.Single(PrometheusExpositionParser.Parse("hits_total 3 # {trace_id=\"abc} oops\n", openMetrics: true).Samples);

        Assert.Equal(3d, sample.Value);
        Assert.Null(sample.Exemplar);
    }

    [Fact]
    public void Parse_ExemplarWithoutTimestamp_HasNullTimestamp()
    {
        var sample = Assert.Single(PrometheusExpositionParser.Parse($"hits_total 3 # {{trace_id=\"{TraceId}\"}} 1\n", openMetrics: true).Samples);

        Assert.Null(sample.Exemplar!.TimestampMillis);
    }

    [Fact]
    public void Parse_OpenMetrics_ReadsSecondsTimestampAsMillis()
    {
        var sample = Assert.Single(PrometheusExpositionParser.Parse("temperature 21.5 1700000000.5\n", openMetrics: true).Samples);

        Assert.Equal(1_700_000_000_500L, sample.TimestampMillis);
    }

    [Fact]
    public void Parse_Classic_StillReadsMillisTimestamp()
    {
        var sample = Assert.Single(PrometheusExpositionParser.Parse("temperature 21.5 1700000000500\n").Samples);

        Assert.Equal(1_700_000_000_500L, sample.TimestampMillis);
    }

    [Fact]
    public void Parse_OpenMetrics_RegistersCounterTypeUnderTotalName()
    {
        var result = PrometheusExpositionParser.Parse("# TYPE http_requests counter\n# HELP http_requests Requests.\nhttp_requests_total 5\n# EOF\n", openMetrics: true);

        Assert.Equal(PrometheusMetricType.Counter, result.Types["http_requests_total"]);
        Assert.Equal("Requests.", result.HelpText["http_requests_total"]);
    }

    [Fact]
    public void Parse_OpenMetrics_DropsCreatedSeriesOfTypedFamilies_ButNotAGaugeNamedCreated()
    {
        var text = "# TYPE http_requests counter\nhttp_requests_total 5\nhttp_requests_created 1700000000\n" +
                   "# TYPE job_created gauge\njob_created 12\n";

        var names = PrometheusExpositionParser.Parse(text, openMetrics: true).Samples.Select(s => s.Name).ToList();

        Assert.Equal(["http_requests_total", "job_created"], names);
    }
}
