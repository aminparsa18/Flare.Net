namespace Flare.Ingest.Model;

/// <summary>
/// Internal representation of a single log record, after mapping from OTLP.
/// </summary>
/// <remarks>
/// This is the shape that drives the ClickHouse <c>logs</c> table
/// (<c>db/clickhouse/0001_logs.sql</c>) — keep the two in sync; a field added here
/// needs a matching column there, and vice versa.
///
/// Proto3 string fields can't distinguish "unset" from "explicitly empty string" on
/// the wire. <see cref="OtlpLogMapper"/> normalizes empty string to <see langword="null"/>
/// for every nullable string field on this type, so a non-null value here is always a
/// meaningful signal from the source. This matters most for <see cref="EventName"/>,
/// where OTel's own spec says *presence* of the field is what marks a record as a named
/// event, not just its content.
/// </remarks>
public sealed record LogEvent
{
    /// <summary>
    /// Flare-internal per-row identifier - not part of the OTLP wire format.
    /// <see cref="OtlpLogMapper"/> generates a fresh <see cref="Guid.NewGuid"/> for
    /// every record it maps. Exists solely so <c>Flare.Api</c>'s search endpoint has a
    /// stable tiebreaker for keyset pagination when two rows share the same
    /// <see cref="Timestamp"/> (see <c>db/clickhouse/0002_logs_event_id.sql</c>) - not
    /// a general-purpose lookup key, and not indexed for point lookups in v1.
    /// </summary>
    public required Guid EventId { get; init; }

    /// <summary>Event time. Falls back to <see cref="ObservedTimestamp"/> if the OTLP record's own time was unset.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Time the collector/receiver observed the event, if the OTLP record set it.</summary>
    public DateTimeOffset? ObservedTimestamp { get; init; }

    /// <summary>
    /// <c>Flare.Ingest</c>'s own wall-clock read (<see cref="TimeProvider.GetUtcNow"/>),
    /// taken once per accepted OTLP export request and stamped on every record it
    /// contains - not from the OTLP wire, unlike every other timestamp on this type.
    /// Unlike <see cref="ObservedTimestamp"/> (itself a client-side clock value in every
    /// path that matters - see ADR-0014), this is the one timestamp on this record Flare
    /// actually trusts as "when did I see this," making <c>IngestedAt - Timestamp</c> the
    /// basis for the clock-skew figures <see cref="Stats.ServiceBreakdown"/> aggregates.
    /// Never used to rewrite <see cref="Timestamp"/> - see ADR-0014's "never rewrite"
    /// decision.
    /// </summary>
    public required DateTimeOffset IngestedAt { get; init; }

    /// <summary>OTLP SeverityNumber (1-24; 0 = unspecified).</summary>
    public required int SeverityNumber { get; init; }

    public string? SeverityText { get; init; }

    public string? Body { get; init; }

    /// <summary>Lower-hex trace id, or null if absent.</summary>
    public string? TraceId { get; init; }

    /// <summary>Lower-hex span id, or null if absent.</summary>
    public string? SpanId { get; init; }

    /// <summary>Low byte of OTLP LogRecord.flags (W3C trace flags).</summary>
    public byte TraceFlags { get; init; }

    /// <summary>Resource attribute "service.name", if present.</summary>
    public string? ServiceName { get; init; }

    /// <summary>The Schema URL of the enclosing OTLP ResourceLogs, if known. Applies to <see cref="ResourceAttributes"/>.</summary>
    public string? ResourceSchemaUrl { get; init; }

    public required IReadOnlyDictionary<string, string> ResourceAttributes { get; init; }

    /// <summary>The Schema URL of the enclosing OTLP ScopeLogs, if known. Applies to the scope fields and <see cref="LogAttributes"/>.</summary>
    public string? ScopeSchemaUrl { get; init; }

    public string? ScopeName { get; init; }

    public string? ScopeVersion { get; init; }

    public required IReadOnlyDictionary<string, string> ScopeAttributes { get; init; }

    public required IReadOnlyDictionary<string, string> LogAttributes { get; init; }

    /// <summary>
    /// OTLP LogRecord.event_name. Presence (non-null) marks this record as a named
    /// OTel "event", per the OTel logs data model — not just descriptive text.
    /// </summary>
    public string? EventName { get; init; }

    /// <summary>
    /// Deterministic id of the Drain cluster <see cref="Body"/> matched to, set by
    /// <see cref="Patterns.LogPatternAnnotator"/> at flush time — not by
    /// <see cref="OtlpLogMapper"/>, unlike every other property here. Empty string (the
    /// default) means unannotated: either the feature is disabled
    /// (<see cref="Patterns.LogPatternOptions.Enabled"/>) or this row predates the
    /// migration/feature and was never backfilled. Not <see langword="required"/>, unlike
    /// <see cref="EventId"/>, so every pre-existing construction site keeps compiling.
    /// </summary>
    public string PatternId { get; init; } = string.Empty;

    /// <summary>The (possibly wildcarded) template text <see cref="PatternId"/> was derived from. See <see cref="PatternId"/>'s remarks.</summary>
    public string PatternTemplate { get; init; } = string.Empty;
}
