using System.Text.Json.Serialization;
using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for <see cref="MetricPointRecord"/>.
/// Same role as <see cref="SpanEventJsonContext"/>: the wire format between
/// <see cref="Sinks.RedisStreamMetricEventSink"/> and <see cref="MetricFlushWorker"/>,
/// internal to this process/Redis instance. Serializing/deserializing through the
/// abstract <see cref="MetricPointRecord"/> type engages its
/// <see cref="System.Text.Json.Serialization.JsonPolymorphicAttribute"/>/
/// <see cref="System.Text.Json.Serialization.JsonDerivedTypeAttribute"/> declarations,
/// so one stream entry round-trips as whichever concrete point type it actually is.
/// </summary>
[JsonSerializable(typeof(MetricPointRecord))]
public sealed partial class MetricEventJsonContext : JsonSerializerContext;
