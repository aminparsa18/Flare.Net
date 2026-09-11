using MemoryPack;

namespace Flare.Api.Model;

/// <summary>Which OTel attribute bag an <see cref="AttributeFilter"/> targets.</summary>
public enum AttributeBag
{
    Log,
    Resource,
    Scope,
}

/// <summary>
/// Comparison an <see cref="AttributeFilter"/> applies between its <see cref="AttributeFilter.Key"/>
/// and <see cref="AttributeFilter.Value"/>. A separate enum from spans' equivalent
/// (<see cref="SpanAttributeFilterOperator"/>), not a shared/reused one - same
/// keep-logs-and-spans-independent reasoning <see cref="SpanAttributeBag"/>'s remarks give
/// for that type, applied here too even though the members themselves don't diverge.
/// </summary>
public enum AttributeFilterOperator
{
    /// <summary>Attribute is present and equals <see cref="AttributeFilter.Value"/> - the default, and the only behavior that existed before this enum did.</summary>
    Equals,

    /// <summary>Attribute is absent, or present with a value other than <see cref="AttributeFilter.Value"/>.</summary>
    NotEquals,

    /// <summary>Attribute key is present in the bag, whatever its value. <see cref="AttributeFilter.Value"/> is ignored.</summary>
    Exists,

    /// <summary>Attribute key is absent from the bag. <see cref="AttributeFilter.Value"/> is ignored.</summary>
    Absent,

    /// <summary>
    /// Attribute is present and its value matches <see cref="AttributeFilter.Value"/> as an
    /// RE2 pattern (ClickHouse <c>match()</c> semantics - see
    /// <c>Query.LogFilterSqlBuilder</c>'s <c>AttributeClause</c>). Appended after
    /// <see cref="Absent"/>, not inserted earlier - MemoryPack encodes this enum as its
    /// numeric ordinal (see <see cref="AttributeFilter.Operator"/>'s own remarks on why its
    /// wire layout only ever grows by appending), so existing members must keep their
    /// ordinals.
    /// </summary>
    Regex,

    /// <summary>Attribute is absent, or present with a value that does not match <see cref="AttributeFilter.Value"/> as an RE2 pattern. Mirrors <see cref="NotEquals"/>'s "missing key counts as no-match" semantics.</summary>
    NotRegex,

    /// <summary>
    /// Attribute is present and its value is one of <see cref="AttributeFilter.Values"/> -
    /// the multi-value counterpart to <see cref="Equals"/>, sparing callers one clause per
    /// candidate value. <see cref="AttributeFilter.Value"/> is ignored; an empty/null
    /// <see cref="AttributeFilter.Values"/> matches nothing (same as an empty ClickHouse
    /// <c>IN</c> list). Appended after <see cref="NotRegex"/>, not inserted earlier - same
    /// ordinal-stability reasoning <see cref="Regex"/> documents for its own addition.
    /// </summary>
    In,

    /// <summary>Attribute is absent, or present with a value that is none of <see cref="AttributeFilter.Values"/>. <see cref="AttributeFilter.Value"/> is ignored; an empty/null <see cref="AttributeFilter.Values"/> matches everything (same as negating an empty ClickHouse <c>IN</c> list) - mirrors <see cref="NotEquals"/>'s "missing key still counts as a non-match" semantics too.</summary>
    NotIn,
}

/// <summary>
/// One filter against a <c>Map(LowCardinality(String), String)</c> column -
/// <c>LogAttributes</c>/<c>ResourceAttributes</c>/<c>ScopeAttributes</c> in
/// <c>db/clickhouse/0001_logs.sql</c>, selected via <see cref="Bag"/>.
/// </summary>
[MemoryPackable]
public sealed partial record AttributeFilter
{
    public AttributeBag Bag { get; init; } = AttributeBag.Log;

    public required string Key { get; init; }

    /// <summary>Ignored (may be left as an empty string) when <see cref="Operator"/> is <see cref="AttributeFilterOperator.Exists"/>, <see cref="AttributeFilterOperator.Absent"/>, <see cref="AttributeFilterOperator.In"/>, or <see cref="AttributeFilterOperator.NotIn"/> - the last two take their operand from <see cref="Values"/> instead.</summary>
    public required string Value { get; init; }

    /// <summary>
    /// Appended after <see cref="Value"/>, not inserted between existing members, so the
    /// MemoryPack wire layout stays backward-compatible with already-deployed dashboards
    /// that still write the original 3-member object (see ADR-0016) - a missing trailing
    /// member just takes this default, reproducing the original equality-only behavior
    /// exactly.
    /// </summary>
    public AttributeFilterOperator Operator { get; init; } = AttributeFilterOperator.Equals;

    /// <summary>
    /// Operand for <see cref="AttributeFilterOperator.In"/>/<see cref="AttributeFilterOperator.NotIn"/> -
    /// ignored (may be left null/empty) for every other operator, same as <see cref="Value"/>
    /// is for <see cref="AttributeFilterOperator.Exists"/>/<see cref="AttributeFilterOperator.Absent"/>.
    /// Appended last (member 5), after <see cref="Operator"/> - not inserted earlier, same
    /// MemoryPack wire-compatibility reasoning <see cref="Operator"/>'s own remarks give: an
    /// already-deployed dashboard still writing only 4 members just takes the default
    /// <c>null</c> here, and a payload missing this member deserializes the same way.
    /// </summary>
    public IReadOnlyList<string>? Values { get; init; }
}

/// <summary>
/// Structured filter shared by both <c>/api/logs/search</c> and
/// <c>/api/logs/aggregate</c> - see <see cref="Query.LogFilterSqlBuilder"/> for how this
/// compiles to a parameterized ClickHouse <c>WHERE</c> clause.
/// </summary>
/// <remarks>
/// <see cref="From"/>/<see cref="To"/> are both optional on the wire; the query
/// builders apply a default/max bounded range so an unfiltered request can't trigger an
/// unbounded scan (see <c>Flare.Api</c>'s README, "Time range defaults").
/// </remarks>
[MemoryPackable]
public sealed partial record LogFilter
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    /// <summary>Exact <c>ServiceName</c> match, ANDed with every other filter. Empty/null = all services.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Exact OTel <c>SeverityNumber</c> match (0-24). Empty/null = all levels.</summary>
    public IReadOnlyList<byte>? SeverityNumbers { get; init; }

    /// <summary>Exact lower-hex <c>TraceId</c> match.</summary>
    public string? TraceId { get; init; }

    /// <summary>Exact lower-hex <c>SpanId</c> match.</summary>
    public string? SpanId { get; init; }

    /// <summary>Exact Drain cluster id match - drives drilling from the Patterns view's "View examples" into the Logs Explorer/search.</summary>
    public string? PatternId { get; init; }

    /// <summary>
    /// Free-text, case-insensitive substring match against <c>Body</c>
    /// (<c>ILIKE '%term%'</c>). See <c>Flare.Api</c>'s README for why this is
    /// substring/case-insensitive rather than token-aligned, and what that costs against
    /// the <c>Body</c> column's <c>tokenbf_v1</c> skip index.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Equality filters over the three attribute bags, ANDed together.</summary>
    public IReadOnlyList<AttributeFilter>? Attributes { get; init; }
}
