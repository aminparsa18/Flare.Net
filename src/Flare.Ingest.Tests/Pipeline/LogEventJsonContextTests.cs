using System.Text.Json;
using Flare.Ingest.Model;
using Flare.Ingest.Pipeline;
using Xunit;

namespace Flare.Ingest.Tests.Pipeline;

/// <summary>
/// Round-trip tests for the wire format <see cref="Sinks.RedisStreamLogEventSink"/> and
/// <see cref="ClickHouseFlushWorker"/> share (serialize into a Redis Stream entry,
/// deserialize back out on flush).
/// </summary>
public class LogEventJsonContextTests
{
    [Fact]
    public void RoundTrips_LogEvent_WithAllOptionalFieldsPopulated()
    {
        var original = new LogEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero),
            ObservedTimestamp = new DateTimeOffset(2026, 8, 7, 12, 0, 1, TimeSpan.Zero),
            IngestedAt = new DateTimeOffset(2026, 8, 7, 12, 0, 2, TimeSpan.Zero),
            SeverityNumber = 17,
            SeverityText = "Error",
            Body = "something went wrong",
            TraceId = "abc123",
            SpanId = "def456",
            TraceFlags = 1,
            ServiceName = "flare-ingest",
            ResourceSchemaUrl = "https://opentelemetry.io/schemas/1.0.0",
            ResourceAttributes = new Dictionary<string, string> { ["service.name"] = "flare-ingest" },
            ScopeSchemaUrl = "https://opentelemetry.io/schemas/1.0.0",
            ScopeName = "Flare.Ingest",
            ScopeVersion = "1.0.0",
            ScopeAttributes = new Dictionary<string, string> { ["scope.key"] = "scope-value" },
            LogAttributes = new Dictionary<string, string> { ["http.method"] = "GET", ["http.status_code"] = "500" },
            EventName = "request.failed",
        };

        AssertRoundTrips(original);
    }

    [Fact]
    public void RoundTrips_LogEvent_WithAllOptionalFieldsNull()
    {
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

        AssertRoundTrips(original);
    }

    /// <summary>
    /// Asserts structural equality field-by-field rather than via <see cref="LogEvent"/>'s
    /// own record-synthesized <c>Equals</c>: the three attribute-map properties are typed
    /// <see cref="IReadOnlyDictionary{TKey, TValue}"/>, whose default equality comparer is
    /// reference equality, not content equality - a fresh <see cref="Dictionary{TKey,TValue}"/>
    /// instance from deserialization would never compare equal to the original that way,
    /// even with identical content. xUnit's <see cref="Assert.Equal{T}(T, T)"/> does compare
    /// dictionaries by content, so asserting per-property (rather than the whole record) is
    /// what actually exercises that correctly.
    /// </summary>
    private static void AssertRoundTrips(LogEvent original)
    {
        var json = JsonSerializer.Serialize(original, LogEventJsonContext.Default.LogEvent);
        var roundTripped = JsonSerializer.Deserialize(json, LogEventJsonContext.Default.LogEvent);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.EventId, roundTripped.EventId);
        Assert.Equal(original.Timestamp, roundTripped.Timestamp);
        Assert.Equal(original.ObservedTimestamp, roundTripped.ObservedTimestamp);
        Assert.Equal(original.IngestedAt, roundTripped.IngestedAt);
        Assert.Equal(original.SeverityNumber, roundTripped.SeverityNumber);
        Assert.Equal(original.SeverityText, roundTripped.SeverityText);
        Assert.Equal(original.Body, roundTripped.Body);
        Assert.Equal(original.TraceId, roundTripped.TraceId);
        Assert.Equal(original.SpanId, roundTripped.SpanId);
        Assert.Equal(original.TraceFlags, roundTripped.TraceFlags);
        Assert.Equal(original.ServiceName, roundTripped.ServiceName);
        Assert.Equal(original.ResourceSchemaUrl, roundTripped.ResourceSchemaUrl);
        Assert.Equal(original.ResourceAttributes, roundTripped.ResourceAttributes);
        Assert.Equal(original.ScopeSchemaUrl, roundTripped.ScopeSchemaUrl);
        Assert.Equal(original.ScopeName, roundTripped.ScopeName);
        Assert.Equal(original.ScopeVersion, roundTripped.ScopeVersion);
        Assert.Equal(original.ScopeAttributes, roundTripped.ScopeAttributes);
        Assert.Equal(original.LogAttributes, roundTripped.LogAttributes);
        Assert.Equal(original.EventName, roundTripped.EventName);
    }
}
