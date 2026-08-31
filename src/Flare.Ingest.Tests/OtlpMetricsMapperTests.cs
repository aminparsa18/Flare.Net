using Flare.Ingest.Model;
using Flare.Ingest.Otlp;
using Google.Protobuf.Collections;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using Xunit;

namespace Flare.Ingest.Tests;

public class OtlpMetricsMapperTests
{
    private static readonly DateTimeOffset TestIngestedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Map_ReturnsEmpty_WhenNoResourceMetrics()
    {
        var request = new ExportMetricsServiceRequest();

        var result = OtlpMetricsMapper.Map(request, TestIngestedAt);

        Assert.Empty(result.Points);
        Assert.Empty(result.UnsupportedMetricNames);
    }

    [Fact]
    public void Map_StampsIngestedAt_FromThePassedInParameter_IndependentOfDataPointTime()
    {
        var metric = new Metric
        {
            Name = "process.threads",
            Gauge = new Gauge { DataPoints = { new NumberDataPoint { AsInt = 1, TimeUnixNano = 1_700_000_000_000_000_000UL } } },
        };

        var record = Assert.Single(OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt).Points);

        Assert.Equal(TestIngestedAt, record.IngestedAt);
        Assert.NotEqual(record.Time, record.IngestedAt);
    }

    [Fact]
    public void Map_MapsGaugeDataPoints_WithDoubleValue()
    {
        var metric = new Metric
        {
            Name = "process.cpu.utilization",
            Gauge = new Gauge
            {
                DataPoints = { new NumberDataPoint { AsDouble = 0.42, TimeUnixNano = 1_700_000_000_000_000_000UL } },
            },
        };

        var record = Assert.IsType<GaugePointRecord>(Assert.Single(OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt).Points));

        Assert.Equal("process.cpu.utilization", record.MetricName);
        Assert.Equal(0.42, record.Value);
    }

    [Fact]
    public void Map_MapsGaugeDataPoints_WithIntValue()
    {
        var metric = new Metric
        {
            Name = "process.threads",
            Gauge = new Gauge
            {
                DataPoints = { new NumberDataPoint { AsInt = 12, TimeUnixNano = 1_700_000_000_000_000_000UL } },
            },
        };

        var record = Assert.IsType<GaugePointRecord>(Assert.Single(OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt).Points));

        Assert.Equal(12d, record.Value);
    }

    [Fact]
    public void Map_MapsSumDataPoints_WithTemporalityAndMonotonicity()
    {
        var metric = new Metric
        {
            Name = "http.server.request.count",
            Sum = new Sum
            {
                AggregationTemporality = AggregationTemporality.Cumulative,
                IsMonotonic = true,
                DataPoints = { new NumberDataPoint { AsDouble = 7, TimeUnixNano = 1_700_000_000_000_000_000UL } },
            },
        };

        var record = Assert.IsType<SumPointRecord>(Assert.Single(OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt).Points));

        Assert.Equal(7d, record.Value);
        Assert.Equal((int)AggregationTemporality.Cumulative, record.AggregationTemporality);
        Assert.True(record.IsMonotonic);
    }

    [Fact]
    public void Map_MapsHistogramDataPoints_WithBucketsAndSum()
    {
        var metric = new Metric
        {
            Name = "http.server.request.duration",
            Histogram = new Histogram
            {
                AggregationTemporality = AggregationTemporality.Cumulative,
                DataPoints =
                {
                    new HistogramDataPoint
                    {
                        TimeUnixNano = 1_700_000_000_000_000_000UL,
                        Count = 3,
                        Sum = 12.5,
                        BucketCounts = { 1UL, 2UL, 0UL },
                        ExplicitBounds = { 10.0, 50.0 },
                    },
                },
            },
        };

        var record = Assert.IsType<HistogramPointRecord>(Assert.Single(OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt).Points));

        Assert.Equal(3UL, record.Count);
        Assert.Equal(12.5, record.Sum);
        Assert.Equal([1UL, 2UL, 0UL], record.BucketCounts);
        Assert.Equal([10.0, 50.0], record.ExplicitBounds);
    }

    [Fact]
    public void Map_HistogramSum_IsNull_WhenAbsentOnTheWire()
    {
        var metric = new Metric
        {
            Name = "http.server.request.duration",
            Histogram = new Histogram
            {
                DataPoints = { new HistogramDataPoint { TimeUnixNano = 1_700_000_000_000_000_000UL, Count = 0 } },
            },
        };

        var record = Assert.IsType<HistogramPointRecord>(Assert.Single(OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt).Points));

        Assert.Null(record.Sum);
    }

    [Fact]
    public void Map_StartTime_IsNull_WhenZeroOnTheWire()
    {
        var metric = new Metric
        {
            Name = "process.threads",
            Gauge = new Gauge
            {
                DataPoints = { new NumberDataPoint { AsInt = 1, TimeUnixNano = 1_700_000_000_000_000_000UL, StartTimeUnixNano = 0 } },
            },
        };

        var record = Assert.Single(OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt).Points);

        Assert.Null(record.StartTime);
    }

    [Fact]
    public void Map_StartTime_IsSet_WhenNonZeroOnTheWire()
    {
        var metric = new Metric
        {
            Name = "process.threads",
            Gauge = new Gauge
            {
                DataPoints = { new NumberDataPoint { AsInt = 1, TimeUnixNano = 1_700_000_000_100_000_000UL, StartTimeUnixNano = 1_700_000_000_000_000_000UL } },
            },
        };

        var record = Assert.Single(OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt).Points);

        Assert.NotNull(record.StartTime);
    }

    [Theory]
    [InlineData(Metric.DataOneofCase.None)]
    public void Map_ReportsUnsupportedMetricName_WhenNoDataIsSet(Metric.DataOneofCase _)
    {
        var metric = new Metric { Name = "unset.metric" };

        var result = OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt);

        Assert.Empty(result.Points);
        Assert.Equal(["unset.metric"], result.UnsupportedMetricNames);
    }

    [Fact]
    public void Map_ReportsUnsupportedMetricName_ForExponentialHistogram()
    {
        var metric = new Metric
        {
            Name = "http.server.request.duration.exp",
            ExponentialHistogram = new ExponentialHistogram
            {
                DataPoints = { new ExponentialHistogramDataPoint { TimeUnixNano = 1_700_000_000_000_000_000UL } },
            },
        };

        var result = OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt);

        Assert.Empty(result.Points);
        Assert.Equal(["http.server.request.duration.exp"], result.UnsupportedMetricNames);
    }

    [Fact]
    public void Map_ReportsUnsupportedMetricName_ForSummary()
    {
        var metric = new Metric
        {
            Name = "legacy.summary",
            Summary = new Summary
            {
                DataPoints = { new SummaryDataPoint { TimeUnixNano = 1_700_000_000_000_000_000UL } },
            },
        };

        var result = OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt);

        Assert.Empty(result.Points);
        Assert.Equal(["legacy.summary"], result.UnsupportedMetricNames);
    }

    [Fact]
    public void Map_ExtractsServiceName_FromResourceAttributes()
    {
        var metric = new Metric
        {
            Name = "process.threads",
            Gauge = new Gauge { DataPoints = { new NumberDataPoint { AsInt = 1, TimeUnixNano = 1_700_000_000_000_000_000UL } } },
        };

        var result = OtlpMetricsMapper.Map(
            SingleMetricRequest(metric, resource: attrs =>
                attrs.Add(new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "payments-api" } })),
            TestIngestedAt);

        var record = Assert.Single(result.Points);
        Assert.Equal("payments-api", record.ServiceName);
        Assert.Equal("payments-api", record.ResourceAttributes["service.name"]);
    }

    [Fact]
    public void Map_FlattensAllAnyValueVariants_OnDataPointAttributes()
    {
        var metric = new Metric
        {
            Name = "process.threads",
            Gauge = new Gauge
            {
                DataPoints =
                {
                    new NumberDataPoint
                    {
                        AsInt = 1,
                        TimeUnixNano = 1_700_000_000_000_000_000UL,
                        Attributes =
                        {
                            new KeyValue { Key = "str", Value = new AnyValue { StringValue = "hello" } },
                            new KeyValue { Key = "bool", Value = new AnyValue { BoolValue = true } },
                        },
                    },
                },
            },
        };

        var record = Assert.Single(OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt).Points);

        Assert.Equal("hello", record.DataPointAttributes["str"]);
        Assert.Equal("true", record.DataPointAttributes["bool"]);
    }

    [Fact]
    public void Map_NormalizesEmptyDescriptionAndUnit_ToNull()
    {
        var metric = new Metric
        {
            Name = "process.threads",
            Description = "",
            Unit = "",
            Gauge = new Gauge { DataPoints = { new NumberDataPoint { AsInt = 1, TimeUnixNano = 1_700_000_000_000_000_000UL } } },
        };

        var record = Assert.Single(OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt).Points);

        Assert.Null(record.Description);
        Assert.Null(record.Unit);
    }

    [Fact]
    public void Map_HandlesMultipleMetricTypes_InOneExport()
    {
        var request = new ExportMetricsServiceRequest
        {
            ResourceMetrics =
            {
                new ResourceMetrics
                {
                    Resource = new Resource(),
                    ScopeMetrics =
                    {
                        new ScopeMetrics
                        {
                            Scope = new InstrumentationScope { Name = "test-scope" },
                            Metrics =
                            {
                                new Metric { Name = "gauge.metric", Gauge = new Gauge { DataPoints = { new NumberDataPoint { AsInt = 1, TimeUnixNano = 1 } } } },
                                new Metric { Name = "sum.metric", Sum = new Sum { DataPoints = { new NumberDataPoint { AsInt = 1, TimeUnixNano = 1 } } } },
                                new Metric { Name = "histogram.metric", Histogram = new Histogram { DataPoints = { new HistogramDataPoint { TimeUnixNano = 1 } } } },
                            },
                        },
                    },
                },
            },
        };

        var result = OtlpMetricsMapper.Map(request, TestIngestedAt);

        Assert.Equal(3, result.Points.Count);
        Assert.IsType<GaugePointRecord>(result.Points[0]);
        Assert.IsType<SumPointRecord>(result.Points[1]);
        Assert.IsType<HistogramPointRecord>(result.Points[2]);
        Assert.Empty(result.UnsupportedMetricNames);
    }

    private static ExportMetricsServiceRequest SingleMetricRequest(
        Metric metric,
        Action<RepeatedField<KeyValue>>? resource = null)
    {
        var resourceMessage = new Resource();
        resource?.Invoke(resourceMessage.Attributes);

        return new ExportMetricsServiceRequest
        {
            ResourceMetrics =
            {
                new ResourceMetrics
                {
                    Resource = resourceMessage,
                    ScopeMetrics =
                    {
                        new ScopeMetrics
                        {
                            Scope = new InstrumentationScope { Name = "test-scope" },
                            Metrics = { metric },
                        },
                    },
                },
            },
        };
    }
}
