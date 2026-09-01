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
/// Span Links are deliberately omitted from this model entirely, not just from
/// storage — same "add it when there's a concrete need" precedent as the alerting
/// roadmap item's per-channel columns. Add a <c>Links</c> property (and the matching
/// <c>ALTER TABLE</c> migration) only once a feature actually consumes them.
/// </remarks>
public sealed record SpanRecord
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
}

/// <summary>A single OTLP Span.Event - a timestamped annotation on a span.</summary>
public sealed record SpanEvent
{
    public required DateTimeOffset Timestamp { get; init; }

    public string? Name { get; init; }

    public required IReadOnlyDictionary<string, string> Attributes { get; init; }
}
