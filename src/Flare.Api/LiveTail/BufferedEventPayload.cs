using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MemoryPack;

namespace Flare.Api.LiveTail;

/// <summary>
/// Decode-only counterpart to <c>Flare.Ingest.Pipeline.RedisEventPayload</c> (ADR-0017) -
/// <see cref="LogTailBroadcaster"/> only ever reads the <c>flare:logs</c> Redis Stream, it
/// never writes to it (<c>Flare.Ingest</c>'s <c>RedisStreamLogEventSink</c> is the only
/// writer), so this mirrors just the <c>Decode</c> half of that codec rather than pulling
/// in a project reference to <c>Flare.Ingest</c> - same "read side doesn't pull in the
/// write side's dependency graph" convention <see cref="BufferedLogEvent"/> already
/// documents against its source type.
/// </summary>
/// <remarks>
/// <b>Must be kept byte-for-byte in sync</b> with <c>Flare.Ingest.Pipeline.RedisEventPayload</c>'s
/// tag byte and fallback behavior - <see cref="LogTailBroadcaster"/> is a second,
/// independent reader of the exact same stream entries <c>ClickHouseFlushWorker</c>
/// consumes, so a payload that decodes for one must decode for the other. Only takes the
/// MemoryPack branch when the literal leading tag byte is present; anything else is handed
/// to <paramref name="jsonTypeInfo"/> (parameter name in <see cref="Decode{T}"/>)
/// unmodified as the pre-ADR-0017 JSON blob a still-draining entry buffered before that
/// migration landed.
/// </remarks>
public static class BufferedEventPayload
{
    private const byte MemoryPackTag = 0x01;

    /// <summary>
    /// Decodes a payload written by <c>Flare.Ingest.Pipeline.RedisEventPayload.Encode</c>,
    /// or - only for entries a pre-ADR-0017 instance buffered before that format existed -
    /// the legacy JSON encoding via <paramref name="jsonTypeInfo"/>.
    /// </summary>
    /// <exception cref="JsonException">
    /// The payload was malformed, in either format. A malformed MemoryPack payload throws
    /// <see cref="MemoryPackSerializationException"/>, not <see cref="JsonException"/> -
    /// rewrapped here (same fix <c>RedisEventPayload.Decode</c> and
    /// <c>Flare.Api.Json.ApiSerialization.ReadAsync</c> already apply for the same reason)
    /// so <see cref="LogTailBroadcaster"/>'s existing <c>catch (JsonException ex)</c> keeps
    /// working unmodified rather than needing to learn a second exception type.
    /// </exception>
    public static T Decode<T>(ReadOnlyMemory<byte> raw, JsonTypeInfo<T> jsonTypeInfo)
    {
        var span = raw.Span;
        if (span.Length > 0 && span[0] == MemoryPackTag)
        {
            try
            {
                return MemoryPackSerializer.Deserialize<T>(span[1..])
                    ?? throw new JsonException("MemoryPack payload deserialized to null.");
            }
            catch (MemoryPackSerializationException ex)
            {
                throw new JsonException("Malformed MemoryPack payload.", ex);
            }
        }

        return JsonSerializer.Deserialize(span, jsonTypeInfo)
            ?? throw new JsonException("JSON payload deserialized to null.");
    }
}
