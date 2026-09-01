using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// API-facing shape of one row from <c>clickhousedb.logs</c> - deliberately a separate
/// type from <c>Flare.Ingest.Model.LogEvent</c> rather than a shared/referenced one, so
/// the read side (this project) doesn't pull in the write side's OTLP/gRPC/Redis
/// dependency graph just to borrow a model shape. Field-for-field mirror of the DDL
/// (<c>db/clickhouse/0001_logs.sql</c> + <c>0002_logs_event_id.sql</c>) - keep the three
/// in sync, same convention <c>LogEvent</c> itself already documents against the DDL —
/// with one deliberate exception: <see cref="SpanDurationNano"/>, a computed follow-up
/// value rather than a stored column.
/// </summary>
/// <remarks>
/// Every DDL column is non-<c>Nullable</c> (see that migration's "Empty string / NULL
/// convention"), so every string property here is non-nullable too - an absent value on
/// the OTel side is stored, and returned, as <see cref="string.Empty"/>, not <c>null</c>.
/// </remarks>
[MemoryPackable]
public sealed partial record LogEventDto
{
    public required Guid EventId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required DateTimeOffset ObservedTimestamp { get; init; }

    /// <summary>
    /// <c>Flare.Ingest</c>'s own receipt-time read, not from the OTLP wire - see
    /// <c>Flare.Ingest.Model.LogEvent.IngestedAt</c>'s remarks and ADR-0014. Distinct
    /// from <see cref="ObservedTimestamp"/>, which is a client-clock value.
    /// </summary>
    public required DateTimeOffset IngestedAt { get; init; }

    public required string TraceId { get; init; }

    public required string SpanId { get; init; }

    public required byte TraceFlags { get; init; }

    public required string SeverityText { get; init; }

    public required byte SeverityNumber { get; init; }

    public required string ServiceName { get; init; }

    public required string Body { get; init; }

    public required string ResourceSchemaUrl { get; init; }

    public required IReadOnlyDictionary<string, string> ResourceAttributes { get; init; }

    public required string ScopeSchemaUrl { get; init; }

    public required string ScopeName { get; init; }

    public required string ScopeVersion { get; init; }

    public required IReadOnlyDictionary<string, string> ScopeAttributes { get; init; }

    public required IReadOnlyDictionary<string, string> LogAttributes { get; init; }

    public required string EventName { get; init; }

    /// <summary>Drain cluster id set by <c>Flare.Ingest</c>'s <c>LogPatternAnnotator</c> at flush time. Empty string = unannotated (feature disabled, or this row predates the migration).</summary>
    public required string PatternId { get; init; }

    /// <summary>The (possibly wildcarded) template text <see cref="PatternId"/> was derived from.</summary>
    public required string PatternTemplate { get; init; }

    /// <summary>
    /// Duration (nanoseconds) of this event's enclosing span - i.e. <c>spans.DurationNano</c>
    /// for the matching <c>(TraceId, SpanId)</c> row. Populated only when the request set
    /// <see cref="LogSearchRequest.IncludeSpanDuration"/> (Flare's Logs Explorer, not
    /// Patterns' drill-down or the CSV export path - see
    /// <see cref="Query.LogQueryService.SearchAsync"/>'s follow-up query), and even then
    /// only for rows whose <c>(TraceId, SpanId)</c> pair matches an already-flushed span -
    /// most commonly <see langword="null"/> for a log line emitted mid-span, before that
    /// span's own duration is known (see Planning.md's "Logs: correlate a log event to
    /// its enclosing span's duration" entry). Same "computed, opt-in, sometimes-null
    /// follow-up field" shape as <see cref="SpanDto.SpanCount"/> - see that field's
    /// remarks for the general pattern this mirrors.
    /// </summary>
    public ulong? SpanDurationNano { get; init; }
}
