using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// One equality filter against a span's <c>ResourceAttributes</c> map column
/// (<c>Map(LowCardinality(String), String)</c>, <c>db/clickhouse/0007_spans.sql</c>) - the
/// Traces page's Services-tab filter chips (docs-internal/planning/roadmap.md's
/// now-removed "Resource-attribute filtering on the Traces &gt; Services tab" item), e.g.
/// <c>deployment.environment=production</c> or <c>host.name=web-1</c>. Reused as-is across
/// all three Services-tab endpoints - see <see cref="Query.ServiceOverviewQueryBuilder"/>,
/// <see cref="Query.ServiceDependencyQueryBuilder"/>, and
/// <see cref="Query.ServiceCallBreakdownQueryBuilder"/> - so the one set of chips narrows
/// the Table view, Map view, and per-node drill-down together.
/// </summary>
/// <remarks>
/// Deliberately its own type, not a reuse of <see cref="SpanAttributeFilter"/>: this
/// filter only ever targets the <c>Resource</c> bag (there's no per-row span/scope
/// attribute concept at the aggregate, cross-trace scope these queries run at - a
/// service-level rollup has no single span whose own attributes it could read), and is
/// equality-only (no <c>Exists</c>/<c>Absent</c>/<c>NotEquals</c>) - a deliberately
/// minimal first cut per the roadmap item's own wording ("filter chips backed by
/// arbitrary resource-attribute key/value pairs"). <see cref="SpanAttributeFilter"/>'s
/// richer operator set is there as precedent if a real need for it shows up here too.
/// <para>
/// No <see cref="DateTimeOffset"/>/<see cref="System.Text.Json.JsonElement"/>/list member,
/// so this carries <c>[GenerateTypeScript]</c> - same eligibility as
/// <see cref="ExternalCallGroup"/>/<see cref="DatabaseCallGroup"/> despite living inside a
/// hand-written parent list (<c>Flare.Api.csproj</c>'s MemoryPack TypeScript codegen
/// comment).
/// </para>
/// </remarks>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record ResourceAttributeFilter
{
    public required string Key { get; init; }

    public required string Value { get; init; }
}
