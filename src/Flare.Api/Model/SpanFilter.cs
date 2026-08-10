namespace Flare.Api.Model;

/// <summary>Which OTel attribute bag a <see cref="SpanAttributeFilter"/> targets.</summary>
public enum SpanAttributeBag
{
    Span,
    Resource,
    Scope,
}

/// <summary>
/// One equality filter against a <c>Map(LowCardinality(String), String)</c> column -
/// <c>SpanAttributes</c>/<c>ResourceAttributes</c>/<c>ScopeAttributes</c> in
/// <c>db/clickhouse/0007_spans.sql</c>, selected via <see cref="Bag"/>. A separate type
/// from <c>Model.AttributeFilter</c> (logs' equivalent), not a shared/reused one - the
/// two bag enums genuinely differ (<c>Log</c> vs <c>Span</c> as the first-party bag), and
/// keeping them distinct avoids a `LogAttributes` value ever being a valid-looking (but
/// meaningless) choice on a span filter, or vice versa.
/// </summary>
public sealed record SpanAttributeFilter
{
    public SpanAttributeBag Bag { get; init; } = SpanAttributeBag.Span;

    public required string Key { get; init; }

    public required string Value { get; init; }
}

/// <summary>
/// Structured filter for <c>/api/spans/search</c> - see
/// <see cref="Query.SpanFilterSqlBuilder"/> for how this compiles to a parameterized
/// ClickHouse <c>WHERE</c> clause. Deliberately a separate type from <c>LogFilter</c>,
/// not a reuse of it - the fields diverge too much (span kind/status/duration vs. log
/// severity/body search) for a shared shape to stay meaningful for either endpoint.
/// </summary>
public sealed record SpanFilter
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    /// <summary>Exact <c>ServiceName</c> match, ANDed with every other filter. Empty/null = all services.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Exact OTel <c>Span.Kind</c> match (0=unspecified..5=consumer). Empty/null = all kinds.</summary>
    public IReadOnlyList<byte>? Kinds { get; init; }

    /// <summary>
    /// Exact <c>StatusCode</c> match, as the DDL's Enum8 label strings (e.g.
    /// <c>"STATUS_CODE_ERROR"</c>) - not re-encoded as an int, since that's exactly what
    /// the column stores and what <see cref="Model.SpanDto.StatusCode"/> returns, so
    /// filter input and DTO output stay symmetric with no translation layer between them.
    /// </summary>
    public IReadOnlyList<string>? StatusCodes { get; init; }

    /// <summary>Exact lower-hex <c>TraceId</c> match.</summary>
    public string? TraceId { get; init; }

    /// <summary>
    /// When set, only root spans (<c>ParentSpanId = ''</c>) match - what
    /// <c>/api/spans/search</c> uses for a practical "trace list" view (one row per
    /// trace, not one per span).
    /// </summary>
    public bool RootSpansOnly { get; init; }

    /// <summary>Inclusive lower bound on <c>DurationNano</c>, if set.</summary>
    public ulong? MinDurationNano { get; init; }

    /// <summary>Inclusive upper bound on <c>DurationNano</c>, if set.</summary>
    public ulong? MaxDurationNano { get; init; }

    /// <summary>Equality filters over the three attribute bags, ANDed together.</summary>
    public IReadOnlyList<SpanAttributeFilter>? Attributes { get; init; }
}
