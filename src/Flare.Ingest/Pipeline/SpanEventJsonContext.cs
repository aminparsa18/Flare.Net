using System.Text.Json.Serialization;
using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for <see cref="SpanRecord"/>.
/// Same role as <see cref="LogEventJsonContext"/>: the wire format between
/// <see cref="Sinks.RedisStreamSpanEventSink"/> and <see cref="SpanFlushWorker"/>,
/// internal to this process/Redis instance.
/// </summary>
[JsonSerializable(typeof(SpanRecord))]
public sealed partial class SpanEventJsonContext : JsonSerializerContext;
