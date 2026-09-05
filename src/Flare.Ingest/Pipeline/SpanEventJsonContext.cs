using System.Text.Json.Serialization;
using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for <see cref="SpanRecord"/>.
/// Same role as <see cref="LogEventJsonContext"/>: prior to ADR-0017 this was the wire
/// format between <see cref="Sinks.RedisStreamSpanEventSink"/> and <see cref="SpanFlushWorker"/>;
/// MemoryPack (<see cref="RedisEventPayload"/>) is now what every entry this process
/// writes uses. Kept only so <see cref="SpanFlushWorker.TryDeserialize"/> can still read
/// any pre-upgrade JSON entry a prior version already buffered.
/// </summary>
[JsonSerializable(typeof(SpanRecord))]
public sealed partial class SpanEventJsonContext : JsonSerializerContext;
