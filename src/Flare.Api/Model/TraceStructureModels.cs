using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// One named span condition of a <see cref="TraceStructureFilter"/> - the <c>A</c> in
/// <c>A -> B</c>. Every set field must hold (AND); at least one must be set. See
/// docs-internal/adr/0069-structural-trace-queries.md.
/// </summary>
[MemoryPackable]
public sealed partial record TraceSpanCondition
{
    /// <summary>The letter the expression refers to this condition by, <c>A</c>-<c>Z</c>, case-insensitive, unique within the filter.</summary>
    public required string Name { get; init; }

    /// <summary>Exact <c>ServiceName</c>. Null/empty = any service.</summary>
    public string? ServiceName { get; init; }

    /// <summary>Exact span name (the <c>Name</c> column, the operation). Null/empty = any name.</summary>
    public string? SpanName { get; init; }

    /// <summary>Exact <c>StatusCode</c> label (e.g. <c>"STATUS_CODE_ERROR"</c>), same encoding as <see cref="SpanFilter.StatusCodes"/>. Null/empty = any status.</summary>
    public string? StatusCode { get; init; }

    /// <summary>Inclusive lower bound on <c>DurationNano</c>, if set.</summary>
    public ulong? MinDurationNano { get; init; }

    /// <summary>Same filters, operators and semantics as <see cref="SpanFilter.Attributes"/>, ANDed together.</summary>
    public IReadOnlyList<SpanAttributeFilter>? Attributes { get; init; }
}

/// <summary>
/// A structural trace query: named span conditions plus a boolean expression over them and
/// the relationships between their spans. <see cref="SpanFilter.Structure"/> keeps only the
/// spans of traces the expression holds for. Compiled by
/// <see cref="Query.TraceStructureSqlBuilder"/>; the expression grammar is in
/// <see cref="Query.TraceStructureExpression"/>.
/// </summary>
[MemoryPackable]
public sealed partial record TraceStructureFilter
{
    /// <summary>
    /// 1 to <see cref="Query.TraceStructureSqlBuilder.MaxConditions"/> conditions. Ones the
    /// expression doesn't mention are ignored.
    /// </summary>
    public IReadOnlyList<TraceSpanCondition>? Conditions { get; init; }

    /// <summary>
    /// E.g. <c>A -> B AND NOT C</c>. A letter alone means "the trace has a span matching it";
    /// <c>A -> B</c> means a span matching <c>B</c> is a direct child of one matching
    /// <c>A</c>; <c>A => B</c> means a descendant at any depth. <c>AND</c>/<c>&amp;&amp;</c>,
    /// <c>OR</c>/<c>||</c>, <c>NOT</c>/<c>!</c> and parentheses combine them.
    /// </summary>
    public string? Expression { get; init; }
}
