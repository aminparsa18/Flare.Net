using System.Text.Json.Serialization;
using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for <see cref="LogEvent"/>.
/// This is the wire format used between <see cref="Sinks.RedisStreamLogEventSink"/>
/// (serializes into a Redis Stream entry's <c>data</c> field) and
/// <see cref="ClickHouseFlushWorker"/> (deserializes back out on flush) - internal to
/// this process/Redis instance, never exposed externally, so plain PascalCase property
/// names (matching <see cref="LogEvent"/> 1:1) are used rather than a naming policy, to
/// keep buffered entries easy to eyeball directly in Redis while debugging.
/// </summary>
[JsonSerializable(typeof(LogEvent))]
public sealed partial class LogEventJsonContext : JsonSerializerContext;
