using Flare.Ingest.Otlp;
using Google.Protobuf;
using Google.Protobuf.Collections;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using Xunit;

namespace Flare.Ingest.Tests;

public class OtlpLogMapperTests
{
    [Fact]
    public void Map_ReturnsEmpty_WhenNoResourceLogs()
    {
        var request = new ExportLogsServiceRequest();

        var result = OtlpLogMapper.Map(request);

        Assert.Empty(result);
    }

    [Fact]
    public void Map_UsesTimeUnixNano_WhenPresent()
    {
        var request = SingleRecordRequest(record =>
        {
            record.TimeUnixNano = 1_700_000_000_000_000_000UL; // 2023-11-14T22:13:20Z
            record.ObservedTimeUnixNano = 1_800_000_000_000_000_000UL;
        });

        var logEvent = Assert.Single(OtlpLogMapper.Map(request));

        Assert.Equal(DateTimeOffset.UnixEpoch.AddTicks(1_700_000_000_000_000_000L / 100), logEvent.Timestamp);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddTicks(1_800_000_000_000_000_000L / 100), logEvent.ObservedTimestamp);
    }

    [Fact]
    public void Map_FallsBackToObservedTimeUnixNano_WhenTimeUnixNanoIsUnset()
    {
        var request = SingleRecordRequest(record =>
        {
            record.TimeUnixNano = 0;
            record.ObservedTimeUnixNano = 1_700_000_000_000_000_000UL;
        });

        var logEvent = Assert.Single(OtlpLogMapper.Map(request));

        Assert.Equal(DateTimeOffset.UnixEpoch.AddTicks(1_700_000_000_000_000_000L / 100), logEvent.Timestamp);
    }

    [Fact]
    public void Map_ExtractsServiceName_FromResourceAttributes()
    {
        var request = SingleRecordRequest(resource: resourceAttrs =>
        {
            resourceAttrs.Add(new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "flare-ingest" } });
            resourceAttrs.Add(new KeyValue { Key = "deployment.environment", Value = new AnyValue { StringValue = "dev" } });
        });

        var logEvent = Assert.Single(OtlpLogMapper.Map(request));

        Assert.Equal("flare-ingest", logEvent.ServiceName);
        Assert.Equal("flare-ingest", logEvent.ResourceAttributes["service.name"]);
        Assert.Equal("dev", logEvent.ResourceAttributes["deployment.environment"]);
    }

    [Fact]
    public void Map_EncodesTraceAndSpanIds_AsLowerHex()
    {
        var traceId = Convert.FromHexString("0102030405060708090a0b0c0d0e0f10");
        var spanId = Convert.FromHexString("0102030405060708");

        var request = SingleRecordRequest(record =>
        {
            record.TraceId = ByteString.CopyFrom(traceId);
            record.SpanId = ByteString.CopyFrom(spanId);
            record.Flags = 0x01; // low byte = W3C trace flags
        });

        var logEvent = Assert.Single(OtlpLogMapper.Map(request));

        Assert.Equal("0102030405060708090a0b0c0d0e0f10", logEvent.TraceId);
        Assert.Equal("0102030405060708", logEvent.SpanId);
        Assert.Equal((byte)0x01, logEvent.TraceFlags);
    }

    [Fact]
    public void Map_ReturnsNullTraceAndSpanId_WhenAbsent()
    {
        var request = SingleRecordRequest();

        var logEvent = Assert.Single(OtlpLogMapper.Map(request));

        Assert.Null(logEvent.TraceId);
        Assert.Null(logEvent.SpanId);
    }

    [Fact]
    public void Map_FlattensAllAnyValueVariants()
    {
        var request = SingleRecordRequest(record =>
        {
            record.Attributes.Add(new KeyValue { Key = "str", Value = new AnyValue { StringValue = "hello" } });
            record.Attributes.Add(new KeyValue { Key = "bool", Value = new AnyValue { BoolValue = true } });
            record.Attributes.Add(new KeyValue { Key = "int", Value = new AnyValue { IntValue = 42 } });
            record.Attributes.Add(new KeyValue { Key = "double", Value = new AnyValue { DoubleValue = 3.5 } });
            record.Attributes.Add(new KeyValue { Key = "bytes", Value = new AnyValue { BytesValue = ByteString.CopyFrom([1, 2, 3]) } });
            record.Attributes.Add(new KeyValue
            {
                Key = "array",
                Value = new AnyValue
                {
                    ArrayValue = new ArrayValue { Values = { new AnyValue { IntValue = 1 }, new AnyValue { IntValue = 2 } } },
                },
            });
            record.Attributes.Add(new KeyValue
            {
                Key = "kvlist",
                Value = new AnyValue
                {
                    KvlistValue = new KeyValueList
                    {
                        Values = { new KeyValue { Key = "nested", Value = new AnyValue { StringValue = "v" } } },
                    },
                },
            });
        });

        var logEvent = Assert.Single(OtlpLogMapper.Map(request));

        Assert.Equal("hello", logEvent.Attributes["str"]);
        Assert.Equal("true", logEvent.Attributes["bool"]);
        Assert.Equal("42", logEvent.Attributes["int"]);
        Assert.Equal("3.5", logEvent.Attributes["double"]);
        Assert.Equal(Convert.ToBase64String([1, 2, 3]), logEvent.Attributes["bytes"]);
        Assert.Equal("[1,2]", logEvent.Attributes["array"]);
        Assert.Equal("{nested=v}", logEvent.Attributes["kvlist"]);
    }

    [Fact]
    public void Map_CarriesSeverityAndScopeInfo()
    {
        var request = new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    Resource = new Resource(),
                    ScopeLogs =
                    {
                        new ScopeLogs
                        {
                            Scope = new InstrumentationScope { Name = "Serilog", Version = "3.1.0" },
                            LogRecords =
                            {
                                new LogRecord
                                {
                                    SeverityNumber = SeverityNumber.Error,
                                    SeverityText = "Error",
                                    Body = new AnyValue { StringValue = "boom" },
                                },
                            },
                        },
                    },
                },
            },
        };

        var logEvent = Assert.Single(OtlpLogMapper.Map(request));

        Assert.Equal((int)SeverityNumber.Error, logEvent.SeverityNumber);
        Assert.Equal("Error", logEvent.SeverityText);
        Assert.Equal("boom", logEvent.Body);
        Assert.Equal("Serilog", logEvent.ScopeName);
        Assert.Equal("3.1.0", logEvent.ScopeVersion);
    }

    private static ExportLogsServiceRequest SingleRecordRequest(
        Action<LogRecord>? record = null,
        Action<RepeatedField<KeyValue>>? resource = null)
    {
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1_700_000_000_000_000_000UL,
            SeverityNumber = SeverityNumber.Info,
            SeverityText = "Information",
            Body = new AnyValue { StringValue = "hello" },
        };
        record?.Invoke(logRecord);

        var resourceMessage = new Resource();
        resource?.Invoke(resourceMessage.Attributes);

        return new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    Resource = resourceMessage,
                    ScopeLogs =
                    {
                        new ScopeLogs
                        {
                            Scope = new InstrumentationScope { Name = "test-scope" },
                            LogRecords = { logRecord },
                        },
                    },
                },
            },
        };
    }
}