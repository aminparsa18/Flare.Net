using Flare.Ingest.Model;
using Flare.Ingest.Prometheus;
using Xunit;

namespace Flare.Ingest.Tests.Prometheus;

public class PrometheusMetricsMapperTests
{
    private static readonly DateTimeOffset ScrapeTime = DateTimeOffset.Parse("2026-08-22T00:00:00Z");

    private static PrometheusScrapeTargetOptions Target(string job = "node", string url = "http://localhost:9100/metrics", Dictionary<string, string>? labels = null) =>
        new() { Job = job, Url = url, Labels = labels ?? [] };

    [Fact]
    public void Map_MapsCounter_AsMonotonicCumulativeSum()
    {
        var text = "# TYPE http_requests_total counter\nhttp_requests_total{method=\"GET\"} 1027\n";
        var parsed = PrometheusExpositionParser.Parse(text);

        var result = PrometheusMetricsMapper.Map(parsed, Target(), ScrapeTime);

        var point = Assert.IsType<SumPointRecord>(Assert.Single(result.Points));
        Assert.Equal("http_requests_total", point.MetricName);
        Assert.Equal(1027d, point.Value);
        Assert.True(point.IsMonotonic);
        Assert.Equal(2, point.AggregationTemporality); // Cumulative
        Assert.Equal("GET", point.DataPointAttributes["method"]);
    }

    [Fact]
    public void Map_MapsGauge()
    {
        var text = "# TYPE temperature gauge\ntemperature 21.5\n";
        var parsed = PrometheusExpositionParser.Parse(text);

        var result = PrometheusMetricsMapper.Map(parsed, Target(), ScrapeTime);

        var point = Assert.IsType<GaugePointRecord>(Assert.Single(result.Points));
        Assert.Equal(21.5d, point.Value);
    }

    [Fact]
    public void Map_TreatsUntyped_AsGauge()
    {
        var parsed = PrometheusExpositionParser.Parse("mystery 5\n");

        var result = PrometheusMetricsMapper.Map(parsed, Target(), ScrapeTime);

        Assert.IsType<GaugePointRecord>(Assert.Single(result.Points));
    }

    [Fact]
    public void Map_AttributesResourceFromJobAndTargetUrl()
    {
        var parsed = PrometheusExpositionParser.Parse("up 1\n");

        var result = PrometheusMetricsMapper.Map(parsed, Target(job: "node-exporter", url: "http://10.0.0.5:9100/metrics"), ScrapeTime);

        var point = Assert.Single(result.Points);
        Assert.Equal("node-exporter", point.ServiceName);
        Assert.Equal("node-exporter", point.ResourceAttributes["service.name"]);
        Assert.Equal("10.0.0.5:9100", point.ResourceAttributes["service.instance.id"]);
    }

    [Fact]
    public void Map_TargetLabels_OverrideComputedResourceAttributes()
    {
        var parsed = PrometheusExpositionParser.Parse("up 1\n");
        var target = Target(job: "node", labels: new Dictionary<string, string> { ["service.name"] = "overridden" });

        var result = PrometheusMetricsMapper.Map(parsed, target, ScrapeTime);

        var point = Assert.Single(result.Points);
        Assert.Equal("overridden", point.ServiceName);
    }

    [Fact]
    public void Map_ConvertsHistogram_CumulativeBucketsToDeltaBucketCounts()
    {
        var text =
            "# TYPE request_duration_seconds histogram\n" +
            "request_duration_seconds_bucket{le=\"0.1\"} 3\n" +
            "request_duration_seconds_bucket{le=\"0.5\"} 7\n" +
            "request_duration_seconds_bucket{le=\"1\"} 10\n" +
            "request_duration_seconds_bucket{le=\"+Inf\"} 12\n" +
            "request_duration_seconds_sum 8.2\n" +
            "request_duration_seconds_count 12\n";
        var parsed = PrometheusExpositionParser.Parse(text);

        var result = PrometheusMetricsMapper.Map(parsed, Target(), ScrapeTime);

        var point = Assert.IsType<HistogramPointRecord>(Assert.Single(result.Points));
        Assert.Equal("request_duration_seconds", point.MetricName);
        Assert.Equal([0.1, 0.5, 1], point.ExplicitBounds);
        Assert.Equal<ulong>([3, 4, 3, 2], point.BucketCounts);
        Assert.Equal(12UL, point.Count);
        Assert.Equal(8.2d, point.Sum);
        Assert.Equal(2, point.AggregationTemporality); // Cumulative
        Assert.Empty(result.UnsupportedMetricNames);
    }

    [Fact]
    public void Map_Histogram_GroupsByLabelsExcludingLe()
    {
        var text =
            "# TYPE latency histogram\n" +
            "latency_bucket{route=\"/a\",le=\"1\"} 1\n" +
            "latency_bucket{route=\"/a\",le=\"+Inf\"} 2\n" +
            "latency_bucket{route=\"/b\",le=\"1\"} 5\n" +
            "latency_bucket{route=\"/b\",le=\"+Inf\"} 5\n";
        var parsed = PrometheusExpositionParser.Parse(text);

        var result = PrometheusMetricsMapper.Map(parsed, Target(), ScrapeTime);

        Assert.Equal(2, result.Points.Count);
        var routes = result.Points.Cast<HistogramPointRecord>().Select(p => p.DataPointAttributes["route"]).OrderBy(r => r).ToList();
        Assert.Equal(["/a", "/b"], routes);
        Assert.DoesNotContain(result.Points.Cast<HistogramPointRecord>(), p => p.DataPointAttributes.ContainsKey("le"));
    }

    [Fact]
    public void Map_DropsSummary_AsUnsupported()
    {
        var text =
            "# TYPE latency summary\n" +
            "latency{quantile=\"0.5\"} 0.2\n" +
            "latency_sum 12.4\n" +
            "latency_count 100\n";
        var parsed = PrometheusExpositionParser.Parse(text);

        var result = PrometheusMetricsMapper.Map(parsed, Target(), ScrapeTime);

        Assert.Empty(result.Points);
        Assert.Equal(["latency"], result.UnsupportedMetricNames);
    }

    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public void Map_Histogram_CollectsBucketExemplarsOntoThePoint()
    {
        var text = "# TYPE latency histogram\n" +
                   $"latency_bucket{{le=\"0.5\"}} 4 # {{trace_id=\"{TraceId}\",span_id=\"00F067AA0BA902B7\",region=\"eu\"}} 0.43 1700000000.25\n" +
                   "latency_bucket{le=\"+Inf\"} 6 # {note=\"no trace here\"} 9\n" +
                   "latency_sum 3.1\nlatency_count 6\n";

        var result = PrometheusMetricsMapper.Map(PrometheusExpositionParser.Parse(text, openMetrics: true), Target(), ScrapeTime);

        var point = Assert.IsType<HistogramPointRecord>(Assert.Single(result.Points));
        var exemplar = Assert.Single(point.Exemplars!);
        Assert.Equal(0.43d, exemplar.Value);
        Assert.Equal(TraceId, exemplar.TraceId);
        Assert.Equal("00f067aa0ba902b7", exemplar.SpanId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_250L), exemplar.Time);
        Assert.Equal("eu", exemplar.FilteredAttributes["region"]);
        Assert.False(exemplar.FilteredAttributes.ContainsKey("trace_id"));
    }

    [Fact]
    public void Map_Counter_CarriesExemplar_AndNameStaysTheClassicTotalName()
    {
        var text = "# TYPE http_requests counter\n" +
                   $"http_requests_total{{code=\"500\"}} 12 # {{trace_id=\"{TraceId}\"}} 1\n" +
                   "http_requests_created{code=\"500\"} 1700000000\n# EOF\n";

        var result = PrometheusMetricsMapper.Map(PrometheusExpositionParser.Parse(text, openMetrics: true), Target(), ScrapeTime);

        var point = Assert.IsType<SumPointRecord>(Assert.Single(result.Points));
        Assert.Equal("http_requests_total", point.MetricName);
        var exemplar = Assert.Single(point.Exemplars!);
        Assert.Equal(TraceId, exemplar.TraceId);
        Assert.Null(exemplar.SpanId);
        Assert.Equal(ScrapeTime, exemplar.Time); // no timestamp of its own, no sample timestamp: the scrape time
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("zzf92f3577b34da6a3ce929d0e0e4736")]
    public void Map_DropsExemplarWhoseTraceIdIsNotThirtyTwoHexDigits(string traceId)
    {
        var text = $"# TYPE hits counter\nhits_total 1 # {{trace_id=\"{traceId}\"}} 1\n";

        var result = PrometheusMetricsMapper.Map(PrometheusExpositionParser.Parse(text, openMetrics: true), Target(), ScrapeTime);

        Assert.Null(Assert.IsType<SumPointRecord>(Assert.Single(result.Points)).Exemplars);
    }

    [Fact]
    public void Map_CapsExemplarsPerPoint()
    {
        var lines = Enumerable.Range(1, 60).Select(i => $"lat_bucket{{le=\"{i}\"}} {i} # {{trace_id=\"{TraceId}\"}} {i}");
        var text = "# TYPE lat histogram\n" + string.Join('\n', lines) + "\nlat_bucket{le=\"+Inf\"} 60\nlat_count 60\nlat_sum 1\n";

        var result = PrometheusMetricsMapper.Map(PrometheusExpositionParser.Parse(text, openMetrics: true), Target(), ScrapeTime);

        Assert.Equal(50, Assert.IsType<HistogramPointRecord>(Assert.Single(result.Points)).Exemplars!.Count);
    }
}
