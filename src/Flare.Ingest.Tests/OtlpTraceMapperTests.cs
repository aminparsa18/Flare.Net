using Flare.Ingest.Otlp;
using Google.Protobuf;
using Google.Protobuf.Collections;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using Xunit;

namespace Flare.Ingest.Tests;

public class OtlpTraceMapperTests
{
    private static readonly DateTimeOffset TestIngestedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Map_ReturnsEmpty_WhenNoResourceSpans()
    {
        var request = new ExportTraceServiceRequest();

        var result = OtlpTraceMapper.Map(request, TestIngestedAt);

        Assert.Empty(result);
    }

    [Fact]
    public void Map_StampsIngestedAt_FromThePassedInParameter_IndependentOfStartTime()
    {
        var request = SingleSpanRequest(span => span.StartTimeUnixNano = 1_700_000_000_000_000_000UL);

        var record = Assert.Single(OtlpTraceMapper.Map(request, TestIngestedAt));

        Assert.Equal(TestIngestedAt, record.IngestedAt);
        Assert.NotEqual(record.StartTime, record.IngestedAt);
    }

    [Fact]
    public void Map_EncodesTraceSpanAndParentSpanIds_AsLowerHex()
    {
        var traceId = Convert.FromHexString("0102030405060708090a0b0c0d0e0f10");
        var spanId = Convert.FromHexString("a1a2a3a4a5a6a7a8");
        var parentSpanId = Convert.FromHexString("b1b2b3b4b5b6b7b8");

        var request = SingleSpanRequest(span =>
        {
            span.TraceId = ByteString.CopyFrom(traceId);
            span.SpanId = ByteString.CopyFrom(spanId);
            span.ParentSpanId = ByteString.CopyFrom(parentSpanId);
        });

        var record = Assert.Single(OtlpTraceMapper.Map(request, TestIngestedAt));

        Assert.Equal("0102030405060708090a0b0c0d0e0f10", record.TraceId);
        Assert.Equal("a1a2a3a4a5a6a7a8", record.SpanId);
        Assert.Equal("b1b2b3b4b5b6b7b8", record.ParentSpanId);
    }

    [Fact]
    public void Map_ParentSpanIdIsNull_WhenAbsent_MarkingARootSpan()
    {
        var request = SingleSpanRequest();

        var record = Assert.Single(OtlpTraceMapper.Map(request, TestIngestedAt));

        Assert.Null(record.ParentSpanId);
    }

    [Fact]
    public void Map_ComputesDurationFromRawWireNanoseconds_NotFromTruncatedTimestamps()
    {
        // 1 tick = 100ns, so a sub-100ns duration would round to zero if computed from
        // the tick-truncated StartTime/EndTime instead of the raw wire values.
        var request = SingleSpanRequest(span =>
        {
            span.StartTimeUnixNano = 1_700_000_000_000_000_000UL;
            span.EndTimeUnixNano = 1_700_000_000_000_000_037UL; // 37ns later
        });

        var record = Assert.Single(OtlpTraceMapper.Map(request, TestIngestedAt));

        Assert.Equal(37UL, record.DurationNano);
    }

    [Fact]
    public void Map_MapsKindAndStatus()
    {
        var request = SingleSpanRequest(span =>
        {
            span.Kind = Span.Types.SpanKind.Server;
            span.Status = new Status { Code = Status.Types.StatusCode.Error, Message = "boom" };
        });

        var record = Assert.Single(OtlpTraceMapper.Map(request, TestIngestedAt));

        Assert.Equal((int)Span.Types.SpanKind.Server, record.Kind);
        Assert.Equal((int)Status.Types.StatusCode.Error, record.StatusCode);
        Assert.Equal("boom", record.StatusMessage);
    }

    [Fact]
    public void Map_StatusCodeDefaultsToUnset_WhenStatusIsAbsent()
    {
        var request = SingleSpanRequest();

        var record = Assert.Single(OtlpTraceMapper.Map(request, TestIngestedAt));

        Assert.Equal((int)Status.Types.StatusCode.Unset, record.StatusCode);
        Assert.Null(record.StatusMessage);
    }

    [Fact]
    public void Map_ExtractsServiceName_FromResourceAttributes()
    {
        var request = SingleSpanRequest(resource: resourceAttrs =>
        {
            resourceAttrs.Add(new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "payments-api" } });
        });

        var record = Assert.Single(OtlpTraceMapper.Map(request, TestIngestedAt));

        Assert.Equal("payments-api", record.ServiceName);
        Assert.Equal("payments-api", record.ResourceAttributes["service.name"]);
    }

    [Fact]
    public void Map_FlattensAllAnyValueVariants_OnSpanAttributes()
    {
        var request = SingleSpanRequest(span =>
        {
            span.Attributes.Add(new KeyValue { Key = "str", Value = new AnyValue { StringValue = "hello" } });
            span.Attributes.Add(new KeyValue { Key = "bool", Value = new AnyValue { BoolValue = true } });
            span.Attributes.Add(new KeyValue { Key = "int", Value = new AnyValue { IntValue = 42 } });
        });

        var record = Assert.Single(OtlpTraceMapper.Map(request, TestIngestedAt));

        Assert.Equal("hello", record.SpanAttributes["str"]);
        Assert.Equal("true", record.SpanAttributes["bool"]);
        Assert.Equal("42", record.SpanAttributes["int"]);
    }

    [Fact]
    public void Map_MapsEvents_InOrder_WithTimestampNameAndAttributes()
    {
        var request = SingleSpanRequest(span =>
        {
            span.Events.Add(new Span.Types.Event
            {
                TimeUnixNano = 1_700_000_000_000_000_000UL,
                Name = "evt.start",
                Attributes = { new KeyValue { Key = "k1", Value = new AnyValue { StringValue = "v1" } } },
            });
            span.Events.Add(new Span.Types.Event
            {
                TimeUnixNano = 1_700_000_000_100_000_000UL,
                Name = "evt.end",
            });
        });

        var record = Assert.Single(OtlpTraceMapper.Map(request, TestIngestedAt));

        Assert.Equal(2, record.Events.Count);
        Assert.Equal("evt.start", record.Events[0].Name);
        Assert.Equal("v1", record.Events[0].Attributes["k1"]);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddTicks(1_700_000_000_000_000_000L / 100), record.Events[0].Timestamp);
        Assert.Equal("evt.end", record.Events[1].Name);
        Assert.Empty(record.Events[1].Attributes);
    }

    [Fact]
    public void Map_EventsIsEmpty_NotNull_WhenSpanHasNoEvents()
    {
        var request = SingleSpanRequest();

        var record = Assert.Single(OtlpTraceMapper.Map(request, TestIngestedAt));

        Assert.Empty(record.Events);
    }

    [Fact]
    public void Map_NormalizesEmptyStrings_ToNull()
    {
        var request = SingleSpanRequest(span =>
        {
            span.Name = "";
            span.TraceState = "";
        });

        var record = Assert.Single(OtlpTraceMapper.Map(request, TestIngestedAt));

        Assert.Null(record.Name);
        Assert.Null(record.TraceState);
    }

    [Fact]
    public void Map_CarriesSchemaUrlsAndScopeInfo()
    {
        var request = new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = new Resource(),
                    SchemaUrl = "https://opentelemetry.io/schemas/1.27.0",
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Scope = new InstrumentationScope { Name = "test-scope", Version = "1.0" },
                            SchemaUrl = "https://opentelemetry.io/schemas/1.20.0",
                            Spans = { MinimalSpan() },
                        },
                    },
                },
            },
        };

        var record = Assert.Single(OtlpTraceMapper.Map(request, TestIngestedAt));

        Assert.Equal("https://opentelemetry.io/schemas/1.27.0", record.ResourceSchemaUrl);
        Assert.Equal("https://opentelemetry.io/schemas/1.20.0", record.ScopeSchemaUrl);
        Assert.Equal("test-scope", record.ScopeName);
        Assert.Equal("1.0", record.ScopeVersion);
    }

    private static Span MinimalSpan() => new()
    {
        TraceId = ByteString.CopyFrom(Convert.FromHexString("0102030405060708090a0b0c0d0e0f10")),
        SpanId = ByteString.CopyFrom(Convert.FromHexString("a1a2a3a4a5a6a7a8")),
        Name = "test-span",
        StartTimeUnixNano = 1_700_000_000_000_000_000UL,
        EndTimeUnixNano = 1_700_000_000_050_000_000UL,
    };

    private static ExportTraceServiceRequest SingleSpanRequest(
        Action<Span>? span = null,
        Action<RepeatedField<KeyValue>>? resource = null)
    {
        var spanMessage = MinimalSpan();
        span?.Invoke(spanMessage);

        var resourceMessage = new Resource();
        resource?.Invoke(resourceMessage.Attributes);

        return new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = resourceMessage,
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Scope = new InstrumentationScope { Name = "test-scope" },
                            Spans = { spanMessage },
                        },
                    },
                },
            },
        };
    }
}
