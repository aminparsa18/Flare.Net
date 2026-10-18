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
    public void Map_KeepsOnlyExemplarsThatCarryATraceId()
    {
        var traceId = Google.Protobuf.ByteString.CopyFrom(Convert.FromHexString("0af7651916cd43dd8448eb211c80319c"));
        var spanId = Google.Protobuf.ByteString.CopyFrom(Convert.FromHexString("b7ad6b7169203331"));
        var metric = new Metric
        {
            Name = "http.server.request.duration",
            Histogram = new Histogram
            {
                DataPoints =
                {
                    new HistogramDataPoint
                    {
                        TimeUnixNano = 1_700_000_000_000_000_000UL,
                        Exemplars =
                        {
                            new Exemplar
                            {
                                AsDouble = 1.5,
                                TimeUnixNano = 1_700_000_001_000_000_000UL,
                                TraceId = traceId,
                                SpanId = spanId,
                                FilteredAttributes = { new KeyValue { Key = "user", Value = new AnyValue { StringValue = "u1" } } },
                            },
                            new Exemplar { AsInt = 7, TimeUnixNano = 1_700_000_002_000_000_000UL },
                        },
                    },
                },
            },
        };

        var point = Assert.IsType<HistogramPointRecord>(Assert.Single(OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt).Points));

        var exemplar = Assert.Single(point.Exemplars!);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", exemplar.TraceId);
        Assert.Equal("b7ad6b7169203331", exemplar.SpanId);
        Assert.Equal(1.5, exemplar.Value);
        Assert.Equal("u1", exemplar.FilteredAttributes["user"]);
    }

    [Fact]
    public void Map_LeavesExemplarsNull_WhenAPointHasNone()
    {
        var metric = new Metric { Name = "g", Gauge = new Gauge { DataPoints = { new NumberDataPoint { AsInt = 1, TimeUnixNano = 1_700_000_000_000_000_000UL } } } };

        var point = Assert.IsType<GaugePointRecord>(Assert.Single(OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt).Points));

        Assert.Null(point.Exemplars);
    }

    [Fact]
    public void MemoryPack_RoundTripsExemplars_ThroughThePolymorphicBase()
    {
        MetricPointRecord point = new HistogramPointRecord
        {
            MetricName = "h",
            ResourceAttributes = new Dictionary<string, string>(),
            ScopeAttributes = new Dictionary<string, string>(),
            DataPointAttributes = new Dictionary<string, string>(),
            Time = TestIngestedAt,
            IngestedAt = TestIngestedAt,
            AggregationTemporality = 1,
            Count = 1,
            BucketCounts = [1],
            ExplicitBounds = [],
            Exemplars = [new MetricExemplar { Time = TestIngestedAt, Value = 4, TraceId = "ab", FilteredAttributes = new Dictionary<string, string> { ["k"] = "v" } }],
        };

        var back = Assert.IsType<HistogramPointRecord>(MemoryPack.MemoryPackSerializer.Deserialize<MetricPointRecord>(MemoryPack.MemoryPackSerializer.Serialize(point)));

        var exemplar = Assert.Single(back.Exemplars!);
        Assert.Equal("ab", exemplar.TraceId);
        Assert.Equal("v", exemplar.FilteredAttributes["k"]);
    }

    [Fact]
    public void ToExemplarRows_EmitsOneRowPerExemplar_WithThePointsIdentity()
    {
        var point = new SumPointRecord
        {
            MetricName = "orders",
            ServiceName = "shop",
            ResourceAttributes = new Dictionary<string, string>(),
            ScopeAttributes = new Dictionary<string, string>(),
            DataPointAttributes = new Dictionary<string, string> { ["route"] = "/a" },
            Time = TestIngestedAt,
            IngestedAt = TestIngestedAt,
            Value = 1,
            AggregationTemporality = 1,
            IsMonotonic = true,
            Exemplars =
            [
                new MetricExemplar { Time = TestIngestedAt, Value = 2, TraceId = "aa", SpanId = "bb", FilteredAttributes = new Dictionary<string, string>() },
                new MetricExemplar { Time = TestIngestedAt, Value = 3, TraceId = "cc", FilteredAttributes = new Dictionary<string, string>() },
            ],
        };

        var rows = Flare.Ingest.Pipeline.ClickHouseMetricRowMapper.ToExemplarRows([point]);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(Flare.Ingest.Pipeline.ClickHouseMetricRowMapper.ExemplarColumns.Count, r.Length));
        Assert.Equal(["orders", "sum", "shop"], rows[0].Take(3));
        Assert.Equal("cc", rows[1][7]);
        Assert.Equal(string.Empty, rows[1][8]);
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
    public void Map_MapsExponentialHistogramDataPoint()
    {
        var metric = new Metric
        {
            Name = "http.server.request.duration",
            Unit = "s",
            ExponentialHistogram = new ExponentialHistogram
            {
                AggregationTemporality = AggregationTemporality.Delta,
                DataPoints =
                {
                    new ExponentialHistogramDataPoint
                    {
                        TimeUnixNano = 1_700_000_000_000_000_000UL,
                        Count = 7,
                        Sum = 1.25,
                        Scale = 3,
                        ZeroCount = 1,
                        ZeroThreshold = 1e-9,
                        Positive = new ExponentialHistogramDataPoint.Types.Buckets { Offset = -4, BucketCounts = { 2, 0, 3 } },
                        Negative = new ExponentialHistogramDataPoint.Types.Buckets { Offset = 2, BucketCounts = { 1 } },
                        Min = -0.5,
                        Max = 0.9,
                    },
                },
            },
        };

        var result = OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt);

        var record = Assert.IsType<ExponentialHistogramPointRecord>(Assert.Single(result.Points));
        Assert.Empty(result.UnsupportedMetricNames);
        Assert.Equal((int)AggregationTemporality.Delta, record.AggregationTemporality);
        Assert.Equal(7UL, record.Count);
        Assert.Equal(1.25, record.Sum);
        Assert.Equal(3, record.Scale);
        Assert.Equal(1UL, record.ZeroCount);
        Assert.Equal(1e-9, record.ZeroThreshold);
        Assert.Equal(-4, record.PositiveOffset);
        Assert.Equal([2UL, 0UL, 3UL], record.PositiveBucketCounts);
        Assert.Equal(2, record.NegativeOffset);
        Assert.Equal([1UL], record.NegativeBucketCounts);
        Assert.Equal(-0.5, record.Min);
        Assert.Equal(0.9, record.Max);
    }

    [Fact]
    public void Map_ExponentialHistogram_LeavesUnsetOptionalFieldsNull_AndMissingRangesEmpty()
    {
        var metric = new Metric
        {
            Name = "sparse.exp",
            ExponentialHistogram = new ExponentialHistogram
            {
                DataPoints = { new ExponentialHistogramDataPoint { TimeUnixNano = 1_700_000_000_000_000_000UL } },
            },
        };

        var record = Assert.IsType<ExponentialHistogramPointRecord>(Assert.Single(OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt).Points));

        Assert.Null(record.Sum);
        Assert.Null(record.Min);
        Assert.Null(record.Max);
        Assert.Empty(record.PositiveBucketCounts);
        Assert.Empty(record.NegativeBucketCounts);
    }

    public static TheoryData<Metric> NoRecordedValueMetrics()
    {
        const uint noValue = (uint)DataPointFlags.NoRecordedValueMask;
        const ulong t = 1_700_000_000_000_000_000UL;
        return new TheoryData<Metric>
        {
            new Metric { Name = "g", Gauge = new Gauge { DataPoints = { new NumberDataPoint { AsDouble = 5, TimeUnixNano = t }, new NumberDataPoint { Flags = noValue, TimeUnixNano = t + 1 } } } },
            new Metric { Name = "s", Sum = new Sum { DataPoints = { new NumberDataPoint { AsDouble = 5, TimeUnixNano = t }, new NumberDataPoint { Flags = noValue, TimeUnixNano = t + 1 } } } },
            new Metric { Name = "h", Histogram = new Histogram { DataPoints = { new HistogramDataPoint { Count = 1, TimeUnixNano = t }, new HistogramDataPoint { Flags = noValue, TimeUnixNano = t + 1 } } } },
            new Metric { Name = "e", ExponentialHistogram = new ExponentialHistogram { DataPoints = { new ExponentialHistogramDataPoint { Count = 1, TimeUnixNano = t }, new ExponentialHistogramDataPoint { Flags = noValue, TimeUnixNano = t + 1 } } } },
        };
    }

    [Theory]
    [MemberData(nameof(NoRecordedValueMetrics))]
    public void Map_SkipsDataPoints_FlaggedNoRecordedValue(Metric metric)
    {
        var result = OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt);

        // Only the real point survives; the staleness marker isn't stored as a fake 0.
        var record = Assert.Single(result.Points);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddTicks(1_700_000_000_000_000_000L / 100), record.Time);
        Assert.Empty(result.UnsupportedMetricNames);
    }

    [Fact]
    public void Map_KeepsDataPoints_WithOtherFlagBitsSet()
    {
        var metric = new Metric
        {
            Name = "g",
            Gauge = new Gauge { DataPoints = { new NumberDataPoint { AsDouble = 3, Flags = 2, TimeUnixNano = 1_700_000_000_000_000_000UL } } },
        };

        var record = Assert.IsType<GaugePointRecord>(Assert.Single(OtlpMetricsMapper.Map(SingleMetricRequest(metric), TestIngestedAt).Points));

        Assert.Equal(3d, record.Value);
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
