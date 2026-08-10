using System.Text.Json;
using Flare.Ingest.Model;
using Flare.Ingest.Pipeline;
using Xunit;

namespace Flare.Ingest.Tests.Pipeline;

/// <summary>
/// Round-trip tests for the wire format <see cref="Sinks.RedisStreamSpanEventSink"/> and
/// <see cref="SpanFlushWorker"/> share, same role as <see cref="LogEventJsonContextTests"/>.
/// </summary>
public class SpanEventJsonContextTests
{
    [Fact]
    public void RoundTrips_SpanRecord_WithAllOptionalFieldsPopulated()
    {
        var original = new SpanRecord
        {
            TraceId = "0102030405060708090a0b0c0d0e0f10",
            SpanId = "a1a2a3a4a5a6a7a8",
            ParentSpanId = "b1b2b3b4b5b6b7b8",
            TraceState = "vendor=value",
            Name = "POST /checkout",
            Kind = 2,
            StartTime = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 8, 10, 12, 0, 0, 150, TimeSpan.Zero),
            DurationNano = 150_000_000,
            StatusCode = 2,
            StatusMessage = "boom",
            ServiceName = "payments-api",
            ResourceSchemaUrl = "https://opentelemetry.io/schemas/1.0.0",
            ResourceAttributes = new Dictionary<string, string> { ["service.name"] = "payments-api" },
            ScopeSchemaUrl = "https://opentelemetry.io/schemas/1.0.0",
            ScopeName = "manual-test",
            ScopeVersion = "1.0",
            ScopeAttributes = new Dictionary<string, string> { ["scope.key"] = "scope-value" },
            SpanAttributes = new Dictionary<string, string> { ["http.method"] = "POST" },
            Events =
            [
                new SpanEvent
                {
                    Timestamp = new DateTimeOffset(2026, 8, 10, 12, 0, 0, 10, TimeSpan.Zero),
                    Name = "evt.start",
                    Attributes = new Dictionary<string, string> { ["k1"] = "v1" },
                },
            ],
        };

        AssertRoundTrips(original);
    }

    [Fact]
    public void RoundTrips_SpanRecord_WithAllOptionalFieldsNullAndNoEvents()
    {
        var original = new SpanRecord
        {
            TraceId = "0102030405060708090a0b0c0d0e0f10",
            SpanId = "a1a2a3a4a5a6a7a8",
            Kind = 0,
            StartTime = DateTimeOffset.UnixEpoch,
            EndTime = DateTimeOffset.UnixEpoch,
            DurationNano = 0,
            StatusCode = 0,
            ResourceAttributes = new Dictionary<string, string>(),
            ScopeAttributes = new Dictionary<string, string>(),
            SpanAttributes = new Dictionary<string, string>(),
            Events = [],
        };

        AssertRoundTrips(original);
    }

    /// <summary>
    /// Asserts structural equality field-by-field rather than via record-synthesized
    /// <c>Equals</c> - same rationale as <see cref="LogEventJsonContextTests"/>: the
    /// attribute-map properties are <see cref="IReadOnlyDictionary{TKey, TValue}"/>
    /// (reference-equal by default), and <see cref="Events"/> is an
    /// <see cref="IReadOnlyList{T}"/> of a nested record.
    /// </summary>
    private static void AssertRoundTrips(SpanRecord original)
    {
        var json = JsonSerializer.Serialize(original, SpanEventJsonContext.Default.SpanRecord);
        var roundTripped = JsonSerializer.Deserialize(json, SpanEventJsonContext.Default.SpanRecord);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.TraceId, roundTripped.TraceId);
        Assert.Equal(original.SpanId, roundTripped.SpanId);
        Assert.Equal(original.ParentSpanId, roundTripped.ParentSpanId);
        Assert.Equal(original.TraceState, roundTripped.TraceState);
        Assert.Equal(original.Name, roundTripped.Name);
        Assert.Equal(original.Kind, roundTripped.Kind);
        Assert.Equal(original.StartTime, roundTripped.StartTime);
        Assert.Equal(original.EndTime, roundTripped.EndTime);
        Assert.Equal(original.DurationNano, roundTripped.DurationNano);
        Assert.Equal(original.StatusCode, roundTripped.StatusCode);
        Assert.Equal(original.StatusMessage, roundTripped.StatusMessage);
        Assert.Equal(original.ServiceName, roundTripped.ServiceName);
        Assert.Equal(original.ResourceSchemaUrl, roundTripped.ResourceSchemaUrl);
        Assert.Equal(original.ResourceAttributes, roundTripped.ResourceAttributes);
        Assert.Equal(original.ScopeSchemaUrl, roundTripped.ScopeSchemaUrl);
        Assert.Equal(original.ScopeName, roundTripped.ScopeName);
        Assert.Equal(original.ScopeVersion, roundTripped.ScopeVersion);
        Assert.Equal(original.ScopeAttributes, roundTripped.ScopeAttributes);
        Assert.Equal(original.SpanAttributes, roundTripped.SpanAttributes);
        Assert.Equal(original.Events.Count, roundTripped.Events.Count);
        for (var i = 0; i < original.Events.Count; i++)
        {
            Assert.Equal(original.Events[i].Timestamp, roundTripped.Events[i].Timestamp);
            Assert.Equal(original.Events[i].Name, roundTripped.Events[i].Name);
            Assert.Equal(original.Events[i].Attributes, roundTripped.Events[i].Attributes);
        }
    }
}
