using MemoryPack;

namespace Flare.Api.Model;

/// <summary>Which OTel attribute bag a <see cref="SpanAttributeFilter"/> targets.</summary>
public enum SpanAttributeBag
{
    Span,
    Resource,
    Scope,
}

/// <summary>
/// Comparison a <see cref="SpanAttributeFilter"/> applies between its
/// <see cref="SpanAttributeFilter.Key"/> and <see cref="SpanAttributeFilter.Value"/>. A
/// separate enum from logs' equivalent (<see cref="AttributeFilterOperator"/>), not a
/// shared/reused one - same reasoning <see cref="SpanAttributeBag"/>'s remarks give for
/// that type, applied here too even though the members themselves don't diverge.
/// </summary>
public enum SpanAttributeFilterOperator
{
    /// <summary>Attribute is present and equals <see cref="SpanAttributeFilter.Value"/> - the default, and the only behavior that existed before this enum did.</summary>
    Equals,

    /// <summary>Attribute is absent, or present with a value other than <see cref="SpanAttributeFilter.Value"/>.</summary>
    NotEquals,

    /// <summary>Attribute key is present in the bag, whatever its value. <see cref="SpanAttributeFilter.Value"/> is ignored.</summary>
    Exists,

    /// <summary>Attribute key is absent from the bag. <see cref="SpanAttributeFilter.Value"/> is ignored.</summary>
    Absent,

    /// <summary>
    /// Attribute is present and its value matches <see cref="SpanAttributeFilter.Value"/> as
    /// an RE2 pattern (ClickHouse <c>match()</c> semantics - see
    /// <c>Query.SpanFilterSqlBuilder</c>'s <c>AttributeClause</c>). Appended after
    /// <see cref="Absent"/>, not inserted earlier - same ordinal-stability reasoning
    /// <see cref="AttributeFilterOperator.Regex"/> documents for its own identical addition.
    /// </summary>
    Regex,

    /// <summary>Attribute is absent, or present with a value that does not match <see cref="SpanAttributeFilter.Value"/> as an RE2 pattern. Mirrors <see cref="NotEquals"/>'s "missing key counts as no-match" semantics.</summary>
    NotRegex,

    /// <summary>
    /// Attribute is present and its value is one of <see cref="SpanAttributeFilter.Values"/> -
    /// the multi-value counterpart to <see cref="Equals"/>. <see cref="SpanAttributeFilter.Value"/>
    /// is ignored; an empty/null <see cref="SpanAttributeFilter.Values"/> matches nothing
    /// (same as an empty ClickHouse <c>IN</c> list). Appended after <see cref="NotRegex"/>,
    /// not inserted earlier - same ordinal-stability reasoning
    /// <see cref="AttributeFilterOperator.In"/> documents for its own identical addition.
    /// </summary>
    In,

    /// <summary>Attribute is absent, or present with a value that is none of <see cref="SpanAttributeFilter.Values"/>. <see cref="SpanAttributeFilter.Value"/> is ignored; an empty/null <see cref="SpanAttributeFilter.Values"/> matches everything (same as negating an empty ClickHouse <c>IN</c> list).</summary>
    NotIn,
}

/// <summary>
/// One filter against a <c>Map(LowCardinality(String), String)</c> column -
/// <c>SpanAttributes</c>/<c>ResourceAttributes</c>/<c>ScopeAttributes</c> in
/// <c>db/clickhouse/0007_spans.sql</c>, selected via <see cref="Bag"/>. A separate type
/// from <c>Model.AttributeFilter</c> (logs' equivalent), not a shared/reused one - the
/// two bag enums genuinely differ (<c>Log</c> vs <c>Span</c> as the first-party bag), and
/// keeping them distinct avoids a `LogAttributes` value ever being a valid-looking (but
/// meaningless) choice on a span filter, or vice versa.
/// </summary>
[MemoryPackable]
public sealed partial record SpanAttributeFilter
{
    public SpanAttributeBag Bag { get; init; } = SpanAttributeBag.Span;

    public required string Key { get; init; }

    /// <summary>Ignored (may be left as an empty string) when <see cref="Operator"/> is <see cref="SpanAttributeFilterOperator.Exists"/>, <see cref="SpanAttributeFilterOperator.Absent"/>, <see cref="SpanAttributeFilterOperator.In"/>, or <see cref="SpanAttributeFilterOperator.NotIn"/> - the last two take their operand from <see cref="Values"/> instead.</summary>
    public required string Value { get; init; }

    /// <summary>
    /// Appended after <see cref="Value"/>, not inserted between existing members, so the
    /// MemoryPack wire layout stays backward-compatible with already-deployed dashboards
    /// that still write the original 3-member object (see ADR-0016) - a missing trailing
    /// member just takes this default, reproducing the original equality-only behavior
    /// exactly.
    /// </summary>
    public SpanAttributeFilterOperator Operator { get; init; } = SpanAttributeFilterOperator.Equals;

    /// <summary>
    /// Operand for <see cref="SpanAttributeFilterOperator.In"/>/<see cref="SpanAttributeFilterOperator.NotIn"/> -
    /// ignored (may be left null/empty) for every other operator, same as <see cref="Value"/>
    /// is for <see cref="SpanAttributeFilterOperator.Exists"/>/<see cref="SpanAttributeFilterOperator.Absent"/>.
    /// Appended last (member 5), after <see cref="Operator"/> - same MemoryPack
    /// wire-compatibility reasoning <see cref="Operator"/>'s own remarks give.
    /// </summary>
    public IReadOnlyList<string>? Values { get; init; }
}

/// <summary>
/// Structured filter for <c>/api/spans/search</c> - see
/// <see cref="Query.SpanFilterSqlBuilder"/> for how this compiles to a parameterized
/// ClickHouse <c>WHERE</c> clause. Deliberately a separate type from <c>LogFilter</c>,
/// not a reuse of it - the fields diverge too much (span kind/status/duration vs. log
/// severity/body search) for a shared shape to stay meaningful for either endpoint.
/// </summary>
[MemoryPackable]
public sealed partial record SpanFilter
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
