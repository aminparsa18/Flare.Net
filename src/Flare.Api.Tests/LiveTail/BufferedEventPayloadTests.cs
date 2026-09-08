using System.Text.Json;
using Flare.Api.LiveTail;
using MemoryPack;
using Xunit;

namespace Flare.Api.Tests.LiveTail;

/// <summary>
/// Tests for <see cref="BufferedEventPayload"/>'s tag-byte envelope - the decode-only
/// mirror of <c>Flare.Ingest.Pipeline.RedisEventPayload</c> (ADR-0017)
/// <see cref="LogTailBroadcaster"/> uses to read the <c>flare:logs</c> Redis Stream.
/// Payloads here are built by hand with <see cref="MemoryPackSerializer"/> directly
/// (not <c>RedisEventPayload.Encode</c>, which isn't project-referenced - see
/// <see cref="BufferedLogEvent"/>'s remarks) to prove <see cref="BufferedLogEvent"/>'s
/// <c>[MemoryPackable]</c> shape actually decodes a tagged MemoryPack payload the same way
/// <c>Flare.Ingest.Model.LogEvent</c>'s does, the binary counterpart to
/// <c>BufferedLogEventJsonContextTests</c>'s hand-written JSON fixture.
/// </summary>
public class BufferedEventPayloadTests
{
    [Fact]
    public void Decode_RoundTrips_TaggedMemoryPackPayload()
    {
        var original = new BufferedLogEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero),
            ObservedTimestamp = new DateTimeOffset(2026, 9, 8, 12, 0, 1, TimeSpan.Zero),
            IngestedAt = new DateTimeOffset(2026, 9, 8, 12, 0, 2, TimeSpan.Zero),
            SeverityNumber = 17,
            SeverityText = "Error",
            Body = "something went wrong",
            TraceId = "abc123",
            SpanId = "def456",
            TraceFlags = 1,
            ServiceName = "flare-ingest",
            ResourceAttributes = new Dictionary<string, string> { ["service.name"] = "flare-ingest" },
            ScopeAttributes = new Dictionary<string, string>(),
            LogAttributes = new Dictionary<string, string> { ["http.status_code"] = "500" },
            EventName = "request.failed",
        };

        var tagged = ToTaggedMemoryPack(original);

        var decoded = BufferedEventPayload.Decode(tagged, BufferedLogEventJsonContext.Default.BufferedLogEvent);

        Assert.Equal(original.EventId, decoded.EventId);
        Assert.Equal(original.Timestamp, decoded.Timestamp);
        Assert.Equal(original.ObservedTimestamp, decoded.ObservedTimestamp);
        Assert.Equal(original.IngestedAt, decoded.IngestedAt);
        Assert.Equal(original.SeverityNumber, decoded.SeverityNumber);
        Assert.Equal(original.SeverityText, decoded.SeverityText);
        Assert.Equal(original.Body, decoded.Body);
        Assert.Equal(original.TraceId, decoded.TraceId);
        Assert.Equal(original.SpanId, decoded.SpanId);
        Assert.Equal(original.TraceFlags, decoded.TraceFlags);
        Assert.Equal(original.ServiceName, decoded.ServiceName);
        Assert.Equal(original.ResourceAttributes, decoded.ResourceAttributes);
        Assert.Equal(original.LogAttributes, decoded.LogAttributes);
        Assert.Equal(original.EventName, decoded.EventName);
    }

    [Fact]
    public void Decode_FallsBackToJson_ForPreAdr0017Payload()
    {
        // No leading tag byte - simulates an entry buffered by a pre-ADR-0017 Flare.Ingest
        // instance still sitting in the stream when Flare.Api reads it.
        var original = new BufferedLogEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UnixEpoch,
            IngestedAt = DateTimeOffset.UnixEpoch,
            SeverityNumber = 0,
            ResourceAttributes = new Dictionary<string, string>(),
            ScopeAttributes = new Dictionary<string, string>(),
            LogAttributes = new Dictionary<string, string>(),
        };
        var legacyJson = JsonSerializer.SerializeToUtf8Bytes(original, BufferedLogEventJsonContext.Default.BufferedLogEvent);

        var decoded = BufferedEventPayload.Decode(legacyJson, BufferedLogEventJsonContext.Default.BufferedLogEvent);

        Assert.Equal(original.EventId, decoded.EventId);
    }

    [Fact]
    public void Decode_MalformedMemoryPackPayload_ThrowsJsonException()
    {
        // Tag byte present, but the rest isn't a valid MemoryPack-encoded BufferedLogEvent -
        // LogTailBroadcaster.Publish's catch site only knows about JsonException.
        byte[] malformed = [0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

        Assert.Throws<JsonException>(() => BufferedEventPayload.Decode(malformed, BufferedLogEventJsonContext.Default.BufferedLogEvent));
    }

    [Fact]
    public void Decode_MalformedLegacyJsonPayload_ThrowsJsonException()
    {
        byte[] malformed = "not valid json"u8.ToArray();

        Assert.Throws<JsonException>(() => BufferedEventPayload.Decode(malformed, BufferedLogEventJsonContext.Default.BufferedLogEvent));
    }

    /// <summary>Same envelope <c>Flare.Ingest.Pipeline.RedisEventPayload.Encode</c> produces: one leading <c>0x01</c> tag byte, then the raw MemoryPack encoding.</summary>
    private static byte[] ToTaggedMemoryPack(BufferedLogEvent value)
    {
        var body = MemoryPackSerializer.Serialize(value);
        var buffer = new byte[body.Length + 1];
        buffer[0] = 0x01;
        body.CopyTo(buffer.AsSpan(1));
        return buffer;
    }
}
