namespace Flare.Api.Model;

/// <summary>One entry of a span's <c>Events</c> Nested column - a timestamped annotation.</summary>
public sealed record SpanEventDto
{
    public required DateTimeOffset Timestamp { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyDictionary<string, string> Attributes { get; init; }
}

/// <summary>
/// API-facing shape of one row from <c>clickhousedb.spans</c> - deliberately a separate
/// type from <c>Flare.Ingest.Model.SpanRecord</c>, same rationale as <c>LogEventDto</c>
/// vs <c>LogEvent</c> (this project doesn't pull in the write side's OTLP/gRPC/Redis
/// dependency graph just to borrow a model shape). Field-for-field mirror of the DDL
/// (<c>db/clickhouse/0007_spans.sql</c>) - keep the two in sync.
/// </summary>
/// <remarks>
/// Every DDL column is non-<c>Nullable</c> (same "empty string = absent" convention as
/// <c>LogEventDto</c>), so every string property here is non-nullable too -
/// <see cref="ParentSpanId"/> being <see cref="string.Empty"/> is what marks a root
/// span. <see cref="StatusCode"/> is the Enum8's string label as ClickHouse returns it
/// (e.g. <c>"STATUS_CODE_OK"</c>), not re-encoded as an int - see
/// <see cref="SpanFilter.StatusCodes"/>'s remarks for why.
/// </remarks>
public sealed record SpanDto
{
    public required string TraceId { get; init; }

    public required string SpanId { get; init; }

    public required string ParentSpanId { get; init; }

    public required string TraceState { get; init; }

    public required string Name { get; init; }

    public required byte Kind { get; init; }

    public required DateTimeOffset StartTime { get; init; }

    public required DateTimeOffset EndTime { get; init; }

    public required ulong DurationNano { get; init; }

    public required string StatusCode { get; init; }

    public required string StatusMessage { get; init; }

    public required string ServiceName { get; init; }

    public required string ResourceSchemaUrl { get; init; }

    public required IReadOnlyDictionary<string, string> ResourceAttributes { get; init; }

    public required string ScopeSchemaUrl { get; init; }

    public required string ScopeName { get; init; }

    public required string ScopeVersion { get; init; }

    public required IReadOnlyDictionary<string, string> ScopeAttributes { get; init; }

    public required IReadOnlyDictionary<string, string> SpanAttributes { get; init; }

    public required IReadOnlyList<SpanEventDto> Events { get; init; }
}
