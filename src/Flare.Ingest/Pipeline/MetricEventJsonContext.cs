using System.Text.Json.Serialization;
using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for <see cref="MetricPointRecord"/>.
/// Same role as <see cref="SpanEventJsonContext"/>: prior to ADR-0017 this was the wire
/// format between <see cref="Sinks.RedisStreamMetricEventSink"/> and
/// <see cref="MetricFlushWorker"/>; MemoryPack (<see cref="RedisEventPayload"/>, via
/// <see cref="MetricPointRecord"/>'s <see cref="MemoryPack.MemoryPackUnionAttribute"/>
/// declarations) is now what every entry this process writes uses. Kept only so
/// <see cref="MetricFlushWorker.TryDeserialize"/> can still read any pre-upgrade JSON
/// entry a prior version already buffered. Serializing/deserializing through the
/// abstract <see cref="MetricPointRecord"/> type engages its
/// <see cref="System.Text.Json.Serialization.JsonPolymorphicAttribute"/>/
/// <see cref="System.Text.Json.Serialization.JsonDerivedTypeAttribute"/> declarations,
/// so one stream entry round-trips as whichever concrete point type it actually is.
/// </summary>
[JsonSerializable(typeof(MetricPointRecord))]
public sealed partial class MetricEventJsonContext : JsonSerializerContext;
