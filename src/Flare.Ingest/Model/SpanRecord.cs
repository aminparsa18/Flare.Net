using MemoryPack;

namespace Flare.Ingest.Model;

/// <summary>
/// Internal representation of a single OTLP span, after mapping from OTLP.
/// </summary>
/// <remarks>
/// This is the shape that drives the ClickHouse <c>spans</c> table
/// (<c>db/clickhouse/0007_spans.sql</c>) — keep the two in sync, same convention as
/// <see cref="LogEvent"/> and the <c>logs</c> table.
///
/// Proto3 string fields can't distinguish "unset" from "explicitly empty string" on
/// the wire. <see cref="OtlpTraceMapper"/> normalizes empty string to <see langword="null"/>
/// for every nullable string field on this type, same as <see cref="LogEvent"/>.
///
/// <see cref="Links"/> (added via <c>db/clickhouse/0013_span_links.sql</c>) was
/// deliberately omitted from the original model - see that migration's remarks for the
/// "add it when there's a concrete need" precedent it followed and why a trace-linking
/// feature (async/batch/messaging spans linking back to a producer in another trace,
/// the waterfall view surfacing them) is that need.
///
/// <see cref="MemoryPackableAttribute"/>: the MemoryPack wire format for
/// <see cref="Sinks.RedisStreamSpanEventSink"/>/<see cref="Pipeline.SpanFlushWorker"/>'s
/// Redis Stream buffer (ADR-0017), same rationale as <see cref="LogEvent"/>.
/// </remarks>
[MemoryPackable]
public sealed partial record SpanRecord
{
    /// <summary>Lower-hex trace id (16 bytes). Spec-guaranteed present and unique together with <see cref="SpanId"/> - no synthetic id column needed, unlike <see cref="LogEvent.EventId"/>.</summary>
    public required string TraceId { get; init; }

    /// <summary>Lower-hex span id (8 bytes).</summary>
    public required string SpanId { get; init; }

    /// <summary>Lower-hex parent span id, or null for a root span.</summary>
    public string? ParentSpanId { get; init; }

    /// <summary>W3C tracestate, if set.</summary>
    public string? TraceState { get; init; }

    /// <summary>Span operation name.</summary>
    public string? Name { get; init; }

    /// <summary>OTLP Span.Kind (0=unspecified..5=consumer).</summary>
    public required int Kind { get; init; }

    public required DateTimeOffset StartTime { get; init; }

    public required DateTimeOffset EndTime { get; init; }

    /// <summary>
    /// <c>Flare.Ingest</c>'s own wall-clock read, taken once per accepted OTLP export
    /// request and stamped on every span it contains - not from the OTLP wire. Same
    /// field/rationale as <see cref="LogEvent.IngestedAt"/> - see that type's remarks
    /// and ADR-0014.
    /// </summary>
    public required DateTimeOffset IngestedAt { get; init; }

    /// <summary>
    /// End minus start, computed from the raw wire nanoseconds (not from
    /// <see cref="StartTime"/>/<see cref="EndTime"/>, which are tick-truncated to 100ns
    /// - see <see cref="OtlpTraceMapper"/>) so duration keeps full wire precision even
    /// though the stored timestamps don't.
    /// </summary>
    public required ulong DurationNano { get; init; }

    /// <summary>OTLP Status.Code (0=unset, 1=ok, 2=error).</summary>
    public required int StatusCode { get; init; }

    public string? StatusMessage { get; init; }

    /// <summary>Resource attribute "service.name", if present.</summary>
    public string? ServiceName { get; init; }

    public string? ResourceSchemaUrl { get; init; }

    public required IReadOnlyDictionary<string, string> ResourceAttributes { get; init; }

    public string? ScopeSchemaUrl { get; init; }

    public string? ScopeName { get; init; }

    public string? ScopeVersion { get; init; }

    public required IReadOnlyDictionary<string, string> ScopeAttributes { get; init; }

    public required IReadOnlyDictionary<string, string> SpanAttributes { get; init; }

    public required IReadOnlyList<SpanEvent> Events { get; init; }

    /// <summary>
    /// OTLP Span.Links: references from this span to a span in the same or a different
    /// trace (e.g. a queue consumer span linking back to its producer's span). Appended
    /// after <see cref="Events"/> rather than inserted earlier in the type, so the
    /// MemoryPack wire layout stays backward-compatible with anything that already
    /// serialized a <see cref="SpanRecord"/> without it - same "append, don't insert"
    /// convention as <c>Flare.Api</c>'s <c>SpanDto.HasError</c>.
    /// </summary>
    public required IReadOnlyList<SpanLink> Links { get; init; }
}

/// <summary>A single OTLP Span.Event - a timestamped annotation on a span.</summary>
[MemoryPackable]
public sealed partial record SpanEvent
{
    public required DateTimeOffset Timestamp { get; init; }

    public string? Name { get; init; }

    public required IReadOnlyDictionary<string, string> Attributes { get; init; }
}

/// <summary>
/// A single OTLP Span.Link - a reference from a span to another span, in the same or a
/// different trace. Unlike <see cref="SpanEvent"/>, a link carries no timestamp of its
/// own on the wire.
/// </summary>
[MemoryPackable]
public sealed partial record SpanLink
{
    /// <summary>Lower-hex trace id (16 bytes) of the linked-to span, same encoding as <see cref="SpanRecord.TraceId"/>.</summary>
    public required string TraceId { get; init; }

    /// <summary>Lower-hex span id (8 bytes) of the linked-to span.</summary>
    public required string SpanId { get; init; }

    /// <summary>W3C tracestate of the linked-to span, if set.</summary>
    public string? TraceState { get; init; }

    public required IReadOnlyDictionary<string, string> Attributes { get; init; }
}
