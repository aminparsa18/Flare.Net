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
    /// <summary>Event time. Falls back to <see cref="ObservedTimestamp"/> if the OTLP record's own time was unset.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Time the collector/receiver observed the event, if the OTLP record set it.</summary>
    public DateTimeOffset? ObservedTimestamp { get; init; }

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
}
