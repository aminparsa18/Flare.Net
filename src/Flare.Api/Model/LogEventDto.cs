namespace Flare.Api.Model;

/// <summary>
/// API-facing shape of one row from <c>clickhousedb.logs</c> - deliberately a separate
/// type from <c>Flare.Ingest.Model.LogEvent</c> rather than a shared/referenced one, so
/// the read side (this project) doesn't pull in the write side's OTLP/gRPC/Redis
/// dependency graph just to borrow a model shape. Field-for-field mirror of the DDL
/// (<c>db/clickhouse/0001_logs.sql</c> + <c>0002_logs_event_id.sql</c>) - keep the three
/// in sync, same convention <c>LogEvent</c> itself already documents against the DDL.
/// </summary>
/// <remarks>
/// Every DDL column is non-<c>Nullable</c> (see that migration's "Empty string / NULL
/// convention"), so every string property here is non-nullable too - an absent value on
/// the OTel side is stored, and returned, as <see cref="string.Empty"/>, not <c>null</c>.
/// </remarks>
public sealed record LogEventDto
{
    public required Guid EventId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required DateTimeOffset ObservedTimestamp { get; init; }

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
}
