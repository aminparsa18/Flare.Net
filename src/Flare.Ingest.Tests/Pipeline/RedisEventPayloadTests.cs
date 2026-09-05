using System.Text.Json;
using Flare.Ingest.Model;
using Flare.Ingest.Patterns;
using Flare.Ingest.Pipeline;
using Xunit;

namespace Flare.Ingest.Tests.Pipeline;

/// <summary>
/// Tests for <see cref="RedisEventPayload"/>'s tag-byte envelope (ADR-0017): this is now
/// the actual wire format <see cref="Sinks.RedisStreamLogEventSink"/>/
/// <see cref="Sinks.RedisStreamMetricEventSink"/>/<see cref="Sinks.RedisStreamSpanEventSink"/>
/// and their matching flush workers share, plus <see cref="Patterns.RedisPatternClusterStore"/>'s
/// bucket payload (base64-wrapped on top - see that class's own remarks, not duplicated
/// here). Covers a MemoryPack round trip for each wrapped type, the legacy-JSON fallback
/// decode path (only reachable via a payload <see cref="RedisEventPayload.Encode{T}"/>
/// itself never produces any more - see the class remarks), and the malformed-payload
/// rewrap into <see cref="JsonException"/> every existing call site's catch block expects.
/// </summary>
public class RedisEventPayloadTests
{
    [Fact]
    public void Encode_Then_Decode_RoundTrips_LogEvent()
    {
        var original = new LogEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero),
            IngestedAt = new DateTimeOffset(2026, 9, 4, 12, 0, 1, TimeSpan.Zero),
            SeverityNumber = 17,
            SeverityText = "Error",
            Body = "something went wrong",
            ResourceAttributes = new Dictionary<string, string> { ["service.name"] = "flare-ingest" },
            ScopeAttributes = new Dictionary<string, string>(),
            LogAttributes = new Dictionary<string, string> { ["http.status_code"] = "500" },
        };

        var encoded = RedisEventPayload.Encode(original);
        var decoded = RedisEventPayload.Decode(encoded, LogEventJsonContext.Default.LogEvent);

        Assert.Equal(original.EventId, decoded.EventId);
        Assert.Equal(original.Timestamp, decoded.Timestamp);
        Assert.Equal(original.IngestedAt, decoded.IngestedAt);
        Assert.Equal(original.SeverityText, decoded.SeverityText);
        Assert.Equal(original.Body, decoded.Body);
        Assert.Equal(original.ResourceAttributes, decoded.ResourceAttributes);
        Assert.Equal(original.LogAttributes, decoded.LogAttributes);
    }

    [Fact]
    public void Encode_Then_Decode_RoundTrips_SpanRecord_WithEvents()
    {
        var original = new SpanRecord
        {
            TraceId = "abc123",
            SpanId = "def456",
            Kind = 2,
            StartTime = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 9, 4, 12, 0, 1, TimeSpan.Zero),
            IngestedAt = new DateTimeOffset(2026, 9, 4, 12, 0, 2, TimeSpan.Zero),
            DurationNano = 1_000_000_000,
            StatusCode = 1,
            ResourceAttributes = new Dictionary<string, string>(),
            ScopeAttributes = new Dictionary<string, string>(),
            SpanAttributes = new Dictionary<string, string> { ["http.method"] = "GET" },
            Events =
            [
                new SpanEvent
                {
                    Timestamp = new DateTimeOffset(2026, 9, 4, 12, 0, 0, 500, TimeSpan.Zero),
                    Name = "retry",
                    Attributes = new Dictionary<string, string> { ["attempt"] = "2" },
                },
            ],
        };

        var encoded = RedisEventPayload.Encode(original);
        var decoded = RedisEventPayload.Decode(encoded, SpanEventJsonContext.Default.SpanRecord);

        Assert.Equal(original.TraceId, decoded.TraceId);
        Assert.Equal(original.SpanId, decoded.SpanId);
        Assert.Equal(original.DurationNano, decoded.DurationNano);
        Assert.Equal(original.SpanAttributes, decoded.SpanAttributes);
        var decodedEvent = Assert.Single(decoded.Events);
        Assert.Equal("retry", decodedEvent.Name);
        Assert.Equal(original.Events[0].Attributes, decodedEvent.Attributes);
    }

    [Theory]
    [MemberData(nameof(MetricPoints))]
    public void Encode_Then_Decode_RoundTrips_MetricPointRecord_Union(MetricPointRecord original)
    {
        // Encoded/decoded through the abstract base type, same as MetricFlushWorker/
        // RedisStreamMetricEventSink actually do - this is what exercises
        // MetricPointRecord's [MemoryPackUnion] dispatch rather than each concrete
        // type's own formatter.
        var encoded = RedisEventPayload.Encode(original);
        var decoded = RedisEventPayload.Decode(encoded, MetricEventJsonContext.Default.MetricPointRecord);

        Assert.Equal(original.GetType(), decoded.GetType());
        Assert.Equal(original.MetricName, decoded.MetricName);
        Assert.Equal(original.DataPointAttributes, decoded.DataPointAttributes);
        switch (original, decoded)
        {
            case (GaugePointRecord o, GaugePointRecord d):
                Assert.Equal(o.Value, d.Value);
                break;
            case (SumPointRecord o, SumPointRecord d):
                Assert.Equal(o.Value, d.Value);
                Assert.Equal(o.IsMonotonic, d.IsMonotonic);
                break;
            case (HistogramPointRecord o, HistogramPointRecord d):
                Assert.Equal(o.Count, d.Count);
                Assert.Equal(o.BucketCounts, d.BucketCounts);
                Assert.Equal(o.ExplicitBounds, d.ExplicitBounds);
                break;
            default:
                Assert.Fail($"Unexpected point type {decoded.GetType()}.");
                break;
        }
    }

    public static TheoryData<MetricPointRecord> MetricPoints() => new()
    {
        new GaugePointRecord
        {
            MetricName = "system.cpu.utilization",
            ResourceAttributes = new Dictionary<string, string>(),
            ScopeAttributes = new Dictionary<string, string>(),
            DataPointAttributes = new Dictionary<string, string> { ["cpu"] = "0" },
            Time = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero),
            IngestedAt = new DateTimeOffset(2026, 9, 4, 12, 0, 1, TimeSpan.Zero),
            Value = 0.42,
        },
        new SumPointRecord
        {
            MetricName = "http.server.request.count",
            ResourceAttributes = new Dictionary<string, string>(),
            ScopeAttributes = new Dictionary<string, string>(),
            DataPointAttributes = new Dictionary<string, string>(),
            Time = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero),
            IngestedAt = new DateTimeOffset(2026, 9, 4, 12, 0, 1, TimeSpan.Zero),
            Value = 123,
            AggregationTemporality = 2,
            IsMonotonic = true,
        },
        new HistogramPointRecord
        {
            MetricName = "http.server.request.duration",
            ResourceAttributes = new Dictionary<string, string>(),
            ScopeAttributes = new Dictionary<string, string>(),
            DataPointAttributes = new Dictionary<string, string>(),
            Time = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero),
            IngestedAt = new DateTimeOffset(2026, 9, 4, 12, 0, 1, TimeSpan.Zero),
            AggregationTemporality = 2,
            Count = 3,
            Sum = 15.5,
            BucketCounts = [1, 2, 0],
            ExplicitBounds = [1.0, 5.0],
        },
    };

    [Fact]
    public void Encode_Then_Decode_RoundTrips_ClusterRecordArray()
    {
        ClusterRecord[] original = [new ClusterRecord("cluster-1", ["user", "*", "logged", "in"], "pattern-1", 42L)];

        var encoded = RedisEventPayload.Encode(original);
        var decoded = RedisEventPayload.Decode(encoded, PatternClusterRecordJsonContext.Default.ClusterRecordArray);

        var record = Assert.Single(decoded);
        Assert.Equal("cluster-1", record.Id);
        Assert.Equal(original[0].TemplateTokens, record.TemplateTokens);
        Assert.Equal("pattern-1", record.PatternId);
        Assert.Equal(42L, record.LastUsedTicks);
    }

    [Fact]
    public void Decode_FallsBackToJson_ForPreMigrationPayload()
    {
        // Encode.Encode<T> never produces this any more - this simulates an entry a
        // pre-ADR-0017 instance already buffered before an upgrade picked up this change.
        var original = new LogEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UnixEpoch,
            IngestedAt = DateTimeOffset.UnixEpoch,
            SeverityNumber = 0,
            ResourceAttributes = new Dictionary<string, string>(),
            ScopeAttributes = new Dictionary<string, string>(),
            LogAttributes = new Dictionary<string, string>(),
        };
        var legacyJson = JsonSerializer.SerializeToUtf8Bytes(original, LogEventJsonContext.Default.LogEvent);

        var decoded = RedisEventPayload.Decode(legacyJson, LogEventJsonContext.Default.LogEvent);

        Assert.Equal(original.EventId, decoded.EventId);
    }

    [Fact]
    public void Decode_MalformedMemoryPackPayload_ThrowsJsonException()
    {
        // Tag byte present, but the rest isn't a valid MemoryPack-encoded LogEvent -
        // every ClickHouseFlushWorker/MetricFlushWorker/SpanFlushWorker.TryDeserialize
        // catch site only knows about JsonException (predates MemoryPack existing here),
        // same reason Flare.Api.Json.ApiSerialization.ReadAsync rewraps it there too.
        byte[] malformed = [0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

        Assert.Throws<JsonException>(() => RedisEventPayload.Decode(malformed, LogEventJsonContext.Default.LogEvent));
    }

    [Fact]
    public void Decode_MalformedLegacyJsonPayload_ThrowsJsonException()
    {
        byte[] malformed = "not valid json"u8.ToArray();

        Assert.Throws<JsonException>(() => RedisEventPayload.Decode(malformed, LogEventJsonContext.Default.LogEvent));
    }
}
