using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Tempo;

/// <summary>
/// A TraceQL metrics query in Flare terms: the span predicate, what to compute per step, and how to group (ADR-0221).
/// </summary>
/// <param name="Condition">The spanset's predicate; null matches every span.</param>
/// <param name="FunctionName">The TraceQL function's name, <c>rate</c> for example.</param>
/// <param name="IsRate">True for <c>rate()</c>: the count per step is divided by the step's seconds.</param>
/// <param name="Stages">The <c>| topk(n)</c>, <c>| bottomk(n)</c> and comparison stages, applied in order to the series after they are computed (ADR-0224, ADR-0225).</param>
/// <param name="Exemplars">Whether the query itself asked for exemplars; a request can also ask with <c>exemplars=n</c> (ADR-0223).</param>
/// <param name="Ancestor">Set for <c>{ a } &gt;&gt; { b } | rate()</c>: only the spans matching <paramref name="Condition"/> that have a span matching this above them are counted (ADR-0225).</param>
/// <param name="Root">Set when the spanset names the trace's root (<c>rootName</c>, <c>rootServiceName</c>, <c>traceDuration</c>): only spans of traces whose root row matches are counted (ADR-0225).</param>
/// <param name="Compare">Set for <c>compare({ selection })</c> (ADR-0226).</param>
/// <param name="Value">The numeric attribute a value function aggregates (ADR-0222); null is the span's duration, which is reported in seconds.</param>
public sealed record TempoMetricsPlan(
    TraceSpanCondition? Condition,
    SpanMetricFunction Function,
    string FunctionName,
    bool IsRate,
    IReadOnlyList<double> Quantiles,
    IReadOnlyList<SpanGroupBy> GroupBy,
    SpanValueAttribute? Value = null,
    IReadOnlyList<TraceQlMetricsStage>? Stages = null,
    bool Exemplars = false,
    SpanAncestor? Ancestor = null,
    SpanTraceRoot? Root = null,
    SpanCompare? Compare = null)
{
    public IReadOnlyList<TraceQlMetricsStage> Stages { get; } = Stages ?? [];
}

/// <summary>Compiles a parsed <see cref="TraceQlMetricsQuery"/> into a <see cref="TempoMetricsPlan"/>. Pure, no ClickHouse dependency.</summary>
/// <remarks>
/// The spanset goes through <see cref="TempoSearchPlanner"/>, so a metrics query and a search read a predicate the
/// same way (anchored regular expressions, the unscoped-attribute guess). Trace-level fields filter a trace's root
/// row in a search; here they restrict the spans counted to those of traces whose root row matches (ADR-0225).
/// </remarks>
public static class TempoMetricsPlanner
{
    public static TempoMetricsPlan Plan(TraceQlMetricsQuery query)
    {
        IReadOnlyList<TraceQlSpanset> spansets = query.Ancestor is null ? [query.Spanset] : [query.Ancestor, query.Spanset];
        var search = TempoSearchPlanner.Plan(new TraceQlQuery(spansets, query.Ancestor is null ? "A" : query.AncestorDirect ? "A -> B" : "A => B"));
        if (query.Ancestor is not null && search.Conditions.Count != 2)
        {
            throw new TraceQlException("each side of > or >> needs at least one span condition.");
        }

        var function = query.Function switch
        {
            TraceQlMetricsFunction.Rate or TraceQlMetricsFunction.CountOverTime => SpanMetricFunction.Count,
            TraceQlMetricsFunction.QuantileOverTime => SpanMetricFunction.Quantile,
            TraceQlMetricsFunction.AvgOverTime => SpanMetricFunction.Avg,
            TraceQlMetricsFunction.MinOverTime => SpanMetricFunction.Min,
            TraceQlMetricsFunction.MaxOverTime => SpanMetricFunction.Max,
            TraceQlMetricsFunction.HistogramOverTime => SpanMetricFunction.Histogram,
            TraceQlMetricsFunction.Compare => SpanMetricFunction.Compare,
            _ => SpanMetricFunction.Sum,
        };

        return new TempoMetricsPlan(
            search.Conditions.LastOrDefault(),
            function,
            query.FunctionName,
            query.Function == TraceQlMetricsFunction.Rate,
            query.Quantiles,
            query.By.Select(GroupBy).ToArray(),
            query.ValueField is { } value ? ValueAttribute(value) : null,
            query.Stages,
            query.Exemplars,
            query.Ancestor is null ? null : new SpanAncestor(search.Conditions[0], query.AncestorDirect),
            TraceRoot(search),
            query.CompareSelection is { } selection ? PlanCompare(query, selection) : null);
    }

    /// <summary>The selection of <c>compare()</c> is one span predicate, planned like a spanset; trace-level fields have no per-span meaning in it.</summary>
    private static SpanCompare PlanCompare(TraceQlMetricsQuery query, TraceQlSpanset selection)
    {
        // compare({}) selects every span (Traces Drilldown's duration Comparison tab sends it before a range is dragged), so the baseline is empty.
        if (selection.Predicates.Count == 0)
        {
            return new SpanCompare(
                new TraceSpanCondition { Name = "A", MinDurationNano = 0 },
                query.CompareTopN,
                query.CompareStartNanos is { } from ? FromNanos(from) : null,
                query.CompareEndNanos is { } to ? FromNanos(to) : null);
        }

        var plan = TempoSearchPlanner.Plan(new TraceQlQuery([selection], "A"));
        if (plan.Conditions.Count != 1 || plan.RootServices.Count > 0 || plan.RootNames.Count > 0 || plan.MinTraceDurationNano is not null || plan.MaxTraceDurationNano is not null)
        {
            throw new TraceQlException("compare() needs a selection of spans, e.g. compare({ status = error }); rootName, rootServiceName and traceDuration cannot select spans.");
        }

        return new SpanCompare(
            plan.Conditions[0],
            query.CompareTopN,
            query.CompareStartNanos is { } start ? FromNanos(start) : null,
            query.CompareEndNanos is { } end ? FromNanos(end) : null);
    }

    private static DateTimeOffset FromNanos(ulong nanos) =>
        DateTimeOffset.UnixEpoch.AddTicks((long)Math.Min(nanos / 100, (ulong)(DateTimeOffset.MaxValue.Ticks - DateTimeOffset.UnixEpoch.Ticks)));

    private static SpanTraceRoot? TraceRoot(TempoSearchPlan search) =>
        search.RootServices.Count == 0 && search.RootNames.Count == 0 && search.MinTraceDurationNano is null && search.MaxTraceDurationNano is null
            ? null
            : new SpanTraceRoot(search.RootServices, search.RootNames, search.MinTraceDurationNano, search.MaxTraceDurationNano);

    private static SpanGroupBy GroupBy(string field)
    {
        switch (field)
        {
            case "name":
                return new SpanGroupBy(field, SpanGroupKind.Name);
            case "status":
                return new SpanGroupBy(field, SpanGroupKind.Status);
            case "kind":
                return new SpanGroupBy(field, SpanGroupKind.Kind);
        }

        var (bag, key) = Attribute(field);
        return new SpanGroupBy(field, SpanGroupKind.Attribute, bag, key);
    }

    private static SpanValueAttribute ValueAttribute(string field)
    {
        var (bag, key) = Attribute(field);
        return new SpanValueAttribute(field, bag, key);
    }

    /// <summary>Which map an attribute reference reads and under what key; <c>.x</c> makes the same span-or-resource guess a predicate does.</summary>
    private static (SpanAttributeBag Bag, string Key) Attribute(string field)
    {
        if (field.StartsWith("span.", StringComparison.Ordinal))
        {
            return (SpanAttributeBag.Span, field["span.".Length..]);
        }

        if (field.StartsWith("resource.", StringComparison.Ordinal))
        {
            return (SpanAttributeBag.Resource, field["resource.".Length..]);
        }

        var key = field[1..];
        return (TempoSearchPlanner.ResourceNamespaces.Any(p => key.StartsWith(p, StringComparison.Ordinal)) ? SpanAttributeBag.Resource : SpanAttributeBag.Span, key);
    }
}
