using Flare.Api.LiveTail;
using Xunit;

namespace Flare.Api.Tests.LiveTail;

public class BufferedLogEventMapperTests
{
    [Fact]
    public void ToDto_CoalescesEveryNullableString_ToEmptyString()
    {
        var dto = BufferedLogEventMapper.ToDto(MinimalBufferedLogEvent());

        Assert.Equal(string.Empty, dto.TraceId);
        Assert.Equal(string.Empty, dto.SpanId);
        Assert.Equal(string.Empty, dto.SeverityText);
        Assert.Equal(string.Empty, dto.ServiceName);
        Assert.Equal(string.Empty, dto.Body);
        Assert.Equal(string.Empty, dto.ResourceSchemaUrl);
        Assert.Equal(string.Empty, dto.ScopeSchemaUrl);
        Assert.Equal(string.Empty, dto.ScopeName);
        Assert.Equal(string.Empty, dto.ScopeVersion);
        Assert.Equal(string.Empty, dto.EventName);
    }

    [Fact]
    public void ToDto_FallsBackObservedTimestampToTimestamp_WhenObservedTimestampIsNull()
    {
        var timestamp = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
        var bufferedLogEvent = MinimalBufferedLogEvent() with { Timestamp = timestamp, ObservedTimestamp = null };

        var dto = BufferedLogEventMapper.ToDto(bufferedLogEvent);

        Assert.Equal(timestamp, dto.Timestamp);
        Assert.Equal(timestamp, dto.ObservedTimestamp);
    }

    [Fact]
    public void ToDto_UsesObservedTimestamp_WhenSet()
    {
        var timestamp = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
        var observed = timestamp.AddSeconds(5);
        var bufferedLogEvent = MinimalBufferedLogEvent() with { Timestamp = timestamp, ObservedTimestamp = observed };

        var dto = BufferedLogEventMapper.ToDto(bufferedLogEvent);

        Assert.Equal(timestamp, dto.Timestamp);
        Assert.Equal(observed, dto.ObservedTimestamp);
    }

    [Fact]
    public void ToDto_PassesThroughIngestedAt_Directly_NoFallback()
    {
        var ingestedAt = new DateTimeOffset(2026, 8, 7, 12, 0, 5, TimeSpan.Zero);
        var bufferedLogEvent = MinimalBufferedLogEvent() with { IngestedAt = ingestedAt };

        var dto = BufferedLogEventMapper.ToDto(bufferedLogEvent);

        Assert.Equal(ingestedAt, dto.IngestedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    public void ToDto_CastsSeverityNumber_ToByte_AtBoundaryValues(int severityNumber)
    {
        var bufferedLogEvent = MinimalBufferedLogEvent() with { SeverityNumber = severityNumber };

        var dto = BufferedLogEventMapper.ToDto(bufferedLogEvent);

        Assert.Equal((byte)severityNumber, dto.SeverityNumber);
    }

    [Fact]
    public void ToDto_PassesThroughAttributeMaps_AndEventIdAndTraceFlags()
    {
        var eventId = Guid.NewGuid();
        var bufferedLogEvent = MinimalBufferedLogEvent() with
        {
            EventId = eventId,
            TraceFlags = 1,
            ResourceAttributes = new Dictionary<string, string> { ["service.name"] = "flare-ingest" },
            ScopeAttributes = new Dictionary<string, string> { ["scope.key"] = "scope-value" },
            LogAttributes = new Dictionary<string, string> { ["http.method"] = "GET" },
        };

        var dto = BufferedLogEventMapper.ToDto(bufferedLogEvent);

        Assert.Equal(eventId, dto.EventId);
        Assert.Equal((byte)1, dto.TraceFlags);
        Assert.Equal(new Dictionary<string, string> { ["service.name"] = "flare-ingest" }, dto.ResourceAttributes);
        Assert.Equal(new Dictionary<string, string> { ["scope.key"] = "scope-value" }, dto.ScopeAttributes);
        Assert.Equal(new Dictionary<string, string> { ["http.method"] = "GET" }, dto.LogAttributes);
    }

    private static BufferedLogEvent MinimalBufferedLogEvent() => new()
    {
        EventId = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UnixEpoch,
        IngestedAt = DateTimeOffset.UnixEpoch,
        SeverityNumber = 9,
        ResourceAttributes = new Dictionary<string, string>(),
        ScopeAttributes = new Dictionary<string, string>(),
        LogAttributes = new Dictionary<string, string>(),
    };
}
