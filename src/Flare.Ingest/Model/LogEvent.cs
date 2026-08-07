namespace Flare.Ingest.Model;

/// <summary>
/// Internal representation of a single log record, after mapping from OTLP.
/// </summary>
/// <remarks>
/// This is a deliberately minimal preview of the model, scoped to what the OTLP
/// receiver needs to hand off to a sink. The roadmap item "Internal log-event model
/// + ClickHouse schema" owns the real, storage-driving shape of this type (attribute
/// typing, ClickHouse column mapping, etc.) - do not treat this as final.
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

    public required IReadOnlyDictionary<string, string> ResourceAttributes { get; init; }

    public string? ScopeName { get; init; }

    public string? ScopeVersion { get; init; }

    public required IReadOnlyDictionary<string, string> Attributes { get; init; }
}