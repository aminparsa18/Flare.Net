using MemoryPack;

namespace Flare.Api.Model;

/// <summary>One entry of a span's <c>Events</c> Nested column - a timestamped annotation.</summary>
[MemoryPackable]
public sealed partial record SpanEventDto
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
/// (<c>db/clickhouse/0007_spans.sql</c>) - keep the two in sync - with one deliberate
/// exception: <see cref="SpanCount"/>, a computed aggregate rather than a stored column.
/// </summary>
/// <remarks>
/// Every DDL column is non-<c>Nullable</c> (same "empty string = absent" convention as
/// <c>LogEventDto</c>), so every string property here is non-nullable too -
/// <see cref="ParentSpanId"/> being <see cref="string.Empty"/> is what marks a root
/// span. <see cref="StatusCode"/> is the Enum8's string label as ClickHouse returns it
/// (e.g. <c>"STATUS_CODE_OK"</c>), not re-encoded as an int - see
/// <see cref="SpanFilter.StatusCodes"/>'s remarks for why.
/// </remarks>
[MemoryPackable]
public sealed partial record SpanDto
{
    public required string TraceId { get; init; }

    public required string SpanId { get; init; }

    public required string ParentSpanId { get; init; }

    public required string TraceState { get; init; }

    public required string Name { get; init; }

    public required byte Kind { get; init; }

    public required DateTimeOffset StartTime { get; init; }

    public required DateTimeOffset EndTime { get; init; }

    /// <summary>
    /// <c>Flare.Ingest</c>'s own receipt-time read, not from the OTLP wire - see
    /// <c>LogEventDto.IngestedAt</c>'s remarks and ADR-0014.
    /// </summary>
    public required DateTimeOffset IngestedAt { get; init; }

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

    /// <summary>
    /// Total spans sharing this row's <see cref="TraceId"/> - a trace with a 200ms
    /// duration and 2 spans reads very differently from one with the same duration and
    /// 80. Populated only for <see cref="SpanFilter.RootSpansOnly"/> searches (Flare's
    /// "trace list" view; see <see cref="Query.SpanQueryService.SearchAsync"/>'s
    /// follow-up count query) - <see langword="null"/> for every other
    /// <c>/api/spans/search</c> result and for <c>GetTraceAsync</c>'s per-span rows,
    /// where every span of the trace is already in hand and a count would be redundant.
    /// </summary>
    public ulong? SpanCount { get; init; }

    /// <summary>
    /// Whether any span sharing this row's <see cref="TraceId"/> - not just this row
    /// itself - carries <c>StatusCode = STATUS_CODE_ERROR</c>. Exists because
    /// <see cref="StatusCode"/> alone, on a <see cref="SpanFilter.RootSpansOnly"/> row,
    /// only reflects the root span: a trace whose root span succeeds (e.g. a gateway
    /// returning 200) but has an erroring span deeper in the call chain would otherwise
    /// read as healthy in the trace list, only visible once the waterfall is opened.
    /// Populated the same way and under the same condition as <see cref="SpanCount"/> -
    /// see its remarks and <see cref="Query.SpanQueryService.SearchAsync"/>'s follow-up
    /// rollup query - <see langword="null"/> for every other <c>/api/spans/search</c>
    /// result and for <c>GetTraceAsync</c>'s per-span rows, where every span of the trace
    /// is already in hand and a rollup would be redundant.
    /// </summary>
    /// <remarks>
    /// Appended after <see cref="SpanCount"/>, not inserted between existing members, so
    /// the MemoryPack wire layout stays backward-compatible with already-deployed
    /// dashboards that still decode the original member count - see
    /// <see cref="SpanAttributeFilter.Operator"/>'s remarks for the same convention.
    /// </remarks>
    public bool? HasError { get; init; }
}
