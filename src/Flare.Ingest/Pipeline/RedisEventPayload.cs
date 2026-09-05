using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MemoryPack;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Wire envelope shared by every Redis Stream buffer in the batched ClickHouse pipeline
/// (<see cref="Sinks.RedisStreamLogEventSink"/>/<see cref="ClickHouseFlushWorker"/>,
/// <see cref="Sinks.RedisStreamMetricEventSink"/>/<see cref="MetricFlushWorker"/>,
/// <see cref="Sinks.RedisStreamSpanEventSink"/>/<see cref="SpanFlushWorker"/>) - see
/// docs-internal/adr/0017-memorypack-ingest-redis-buffer.md.
/// </summary>
/// <remarks>
/// <see cref="Encode{T}"/> always writes a single leading tag byte
/// (<see cref="MemoryPackTag"/>) followed by the MemoryPack-encoded value.
/// <see cref="Decode{T}"/> only takes the MemoryPack branch when that literal tag byte is
/// present; anything else is handed to <paramref name="jsonTypeInfo"/> unmodified as the
/// pre-migration JSON blob. That byte can never appear at the start of one of the
/// pre-migration JSON payloads (which always start with <c>{</c>/<c>[</c>/ASCII
/// whitespace - never the control byte <c>0x01</c>), so the branch is unambiguous, not a
/// heuristic. This is a one-time upgrade seam, not a permanent second format: every entry
/// this process itself writes carries the tag, so the JSON branch is only ever exercised
/// by entries a pre-upgrade instance already buffered before the deployment picked up
/// this change, and stops being exercised at all once those have drained through
/// <see cref="LogEventPipelineOptions.MaxDeliveryAttempts"/>-bounded normal consumption -
/// without it, every already-buffered-but-unflushed entry at upgrade time would fail to
/// deserialize and eventually be dropped as a poison message (see
/// <see cref="ClickHouseFlushWorker"/>'s remarks), silently losing whatever hadn't been
/// flushed yet.
/// </remarks>
public static class RedisEventPayload
{
    private const byte MemoryPackTag = 0x01;

    /// <summary>Encodes <paramref name="value"/> as a tagged MemoryPack payload - see the class remarks.</summary>
    public static byte[] Encode<T>(T value)
    {
        var body = MemoryPackSerializer.Serialize(value);
        var buffer = new byte[body.Length + 1];
        buffer[0] = MemoryPackTag;
        body.CopyTo(buffer.AsSpan(1));
        return buffer;
    }

    /// <summary>
    /// Decodes a payload written by <see cref="Encode{T}"/>, or - only for entries a
    /// pre-upgrade instance buffered before this format existed - the legacy JSON
    /// encoding via <paramref name="jsonTypeInfo"/>.
    /// </summary>
    /// <exception cref="JsonException">
    /// The payload was malformed, in either format. A malformed MemoryPack payload
    /// throws <see cref="MemoryPackSerializationException"/>, not
    /// <see cref="JsonException"/> - rewrapped here (same fix
    /// <c>Flare.Api.Json.ApiSerialization.ReadAsync</c> already applied for the same
    /// reason) so every existing <c>catch (JsonException ex)</c> call site keeps working
    /// unmodified rather than needing to learn a second exception type.
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
