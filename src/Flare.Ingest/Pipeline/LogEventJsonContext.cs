using System.Text.Json.Serialization;
using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for <see cref="LogEvent"/>.
/// Prior to ADR-0017 this was the wire format between <see cref="Sinks.RedisStreamLogEventSink"/>
/// and <see cref="ClickHouseFlushWorker"/>; MemoryPack (<see cref="RedisEventPayload"/>) is
/// now what every entry this process writes uses. Kept only so
/// <see cref="ClickHouseFlushWorker.TryDeserialize"/> can still read any pre-upgrade JSON
/// entry a prior version already buffered - internal to this process/Redis instance,
/// never exposed externally, so plain PascalCase property names (matching
/// <see cref="LogEvent"/> 1:1) are used rather than a naming policy, to keep buffered
/// entries easy to eyeball directly in Redis while debugging.
/// </summary>
[JsonSerializable(typeof(LogEvent))]
public sealed partial class LogEventJsonContext : JsonSerializerContext;
