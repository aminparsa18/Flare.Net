using Flare.Api.Model;
using Flare.Api.Prometheus;
using Xunit;

namespace Flare.Api.Tests.Prometheus;

public class PromNamesTests
{
    private static MetricNameInfo Info(string name, MetricPointType type, string? unit = null, string service = "api") =>
        new() { MetricName = name, ServiceName = service, Type = type, Unit = unit, SeriesCount = 1 };

    [Theory]
    [InlineData("http.server.request.duration", "s", MetricPointType.Histogram, "http_server_request_duration_seconds")]
    [InlineData("process.runtime.memory", "By", MetricPointType.Gauge, "process_runtime_memory_bytes")]
    [InlineData("cpu.utilization", "1", MetricPointType.Gauge, "cpu_utilization_ratio")]
    [InlineData("queue.depth", "{message}", MetricPointType.Gauge, "queue_depth")]
    [InlineData("request.duration.seconds", "s", MetricPointType.Gauge, "request_duration_seconds")]
    public void BaseName_AppliesUnitSuffix(string otel, string unit, MetricPointType type, string expected) =>
        Assert.Equal(expected, PromNames.BaseName(otel, unit, type));

    [Fact]
    public void Resolve_Counter_AddsTotalSuffix()
    {
        var metric = PromNames.Resolve("http_requests_total", [Info("http.requests", MetricPointType.Sum)]);

        Assert.Equal(new PromMetric("http.requests", MetricPointType.Sum, PromMetricKind.Counter, "http_requests_total"), metric);
    }

    [Theory]
    [InlineData("d_seconds_bucket", "HistogramBucket")]
    [InlineData("d_seconds_sum", "HistogramSum")]
    [InlineData("d_seconds_count", "HistogramCount")]
    [InlineData("d_seconds", "HistogramBucket")]
    public void Resolve_Histogram_Variants(string promName, string kind)
    {
        var metric = PromNames.Resolve(promName, [Info("d", MetricPointType.Histogram, "s")]);

        Assert.Equal(Enum.Parse<PromMetricKind>(kind), metric?.Kind);
    }

    [Fact]
    public void Resolve_SameMetricFromTwoServices_IsOneMetric() =>
        Assert.NotNull(PromNames.Resolve("up", [Info("up", MetricPointType.Gauge), Info("up", MetricPointType.Gauge, service: "web")]));

    [Fact]
    public void Resolve_Unknown_ReturnsNull() =>
        Assert.Null(PromNames.Resolve("nope", [Info("up", MetricPointType.Gauge)]));

    [Fact]
    public void Resolve_CollidingTypes_Throws() =>
        Assert.Throws<PromQlException>(() => PromNames.Resolve("x_total", [Info("x_total", MetricPointType.Gauge), Info("x", MetricPointType.Sum)]));

    [Fact]
    public void LabelName_ReplacesDots() => Assert.Equal("http_route", PromNames.LabelName("http.route"));
}
