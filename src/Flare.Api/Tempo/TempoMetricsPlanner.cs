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
/// <param name="Ancestor">Set for <c>{ a } &gt;&gt; { b } | rate()</c>: only the spans matching <paramref name="Condition"/> that have a span matching this above them (or beside them, or none of those, for the other operators) are counted (ADR-0225, ADR-0231).</param>
/// <param name="Root">Set when the spanset names the trace's root (<c>rootName</c>, <c>rootServiceName</c>, <c>traceDuration</c>): only spans of traces whose root row matches are counted (ADR-0225).</param>
/// <param name="Compare">Set for <c>compare({ selection })</c> (ADR-0226).</param>
/// <param name="Spansets">Set when the query combines spansets with <c>&amp;&amp;</c>, <c>||</c> or a union operator (ADR-0232): the spans counted are its result, and <paramref name="Condition"/> and <paramref name="Ancestor"/> are null.</param>
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
    SpanCompare? Compare = null,
    SpanSetExpression? Spansets = null)
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
        if (query.Combination is { } combination)
        {
            return PlanCombination(query, combination);
        }

        IReadOnlyList<TraceQlSpanset> spansets = query.Ancestor is null ? [query.Spanset] : [query.Ancestor, query.Spanset];
        var search = TempoSearchPlanner.Plan(new TraceQlQuery(spansets, query.Ancestor is null ? "A" : $"A {TraceStructureExpression.Operator(query.AncestorKind, query.AncestorNegated)} B"));
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
            query.Ancestor is null ? null : new SpanAncestor(search.Conditions[0], query.AncestorKind, query.AncestorNegated),
            TraceRoot(search),
            PlanCompare(query));
    }

    /// <summary>
    /// A query of several spansets (ADR-0232): each spanset becomes a condition exactly as in a search (so a value group, an empty side of a structural
    /// operator and the unscoped-attribute guess read the same), and the tree of <c>&amp;&amp;</c>, <c>||</c> and structural operators over them becomes the
    /// <see cref="SpanSetExpression"/> the builder compiles into one predicate on a span.
    /// </summary>
    private static TempoMetricsPlan PlanCombination(TraceQlMetricsQuery query, TraceQlCombination combination)
    {
        var spansets = SpanSets(combination, "the query");

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
            null,
            function,
            query.FunctionName,
            query.Function == TraceQlMetricsFunction.Rate,
            query.Quantiles,
            query.By.Select(GroupBy).ToArray(),
            query.ValueField is { } value ? ValueAttribute(value) : null,
            query.Stages,
            query.Exemplars,
            null,
            null,
            PlanCompare(query),
            spansets);
    }

    /// <summary>
    /// The tree of <paramref name="combination"/> as the <see cref="SpanSetExpression"/> the builder compiles (ADR-0232): every spanset is planned as a search
    /// condition (<paramref name="owner"/> names what it belongs to in an error), and the tree of <c>&amp;&amp;</c>, <c>||</c> and structural operators over
    /// the conditions becomes the expression.
    /// </summary>
    private static SpanSetExpression SpanSets(TraceQlCombination combination, string owner)
    {
        var search = TempoSearchPlanner.Plan(new TraceQlQuery(combination.Spansets, combination.Expression));
        var conditions = search.Conditions.ToDictionary(c => char.ToUpperInvariant(c.Name[0]));

        TraceSpanCondition Condition(char letter) => conditions.TryGetValue(letter, out var condition)
            ? condition
            : throw new TraceQlException($"spanset {letter - 'A' + 1} of {owner} has no span condition; each spanset of a combination needs at least one.");

        SpanSetExpression Node(TraceStructureNode node) => node switch
        {
            TraceStructureHas has => new SpanSetCondition(Condition(has.Condition)),
            TraceStructureRelation r => new SpanSetRelation(
                Condition(r.Parent), Condition(r.Child), r.Kind, r.Negated,
                combination.Relations.Any(x => x.Union && x.Left == r.Parent && x.Right == r.Child)),
            TraceStructureAnd and => new SpanSetAnd(Node(and.Left), Node(and.Right)),
            TraceStructureOr or => new SpanSetOr(Node(or.Left), Node(or.Right)),
            _ => throw new TraceQlException($"{owner} combines spansets with &&, || and the structural operators only."),
        };

        return Node(combination.Tree);
    }

    /// <summary>
    /// The selection of <c>compare()</c>: one span predicate, planned like a spanset, or a combination of spansets (ADR-0234) planned like a metrics query's own;
    /// trace-level fields have no per-span meaning in it. Null for a query that is not <c>compare()</c>.
    /// </summary>
    private static SpanCompare? PlanCompare(TraceQlMetricsQuery query)
    {
        if (query.CompareSelection is not { } selection)
        {
            return null;
        }

        var from = query.CompareStartNanos is { } start ? FromNanos(start) : (DateTimeOffset?)null;
        var to = query.CompareEndNanos is { } end ? FromNanos(end) : (DateTimeOffset?)null;
        if (query.CompareCombination is { } combination)
        {
            return new SpanCompare(SpanSets(combination, "compare()'s selection"), query.CompareTopN, from, to);
        }

        // compare({}) selects every span (Traces Drilldown's duration Comparison tab sends it before a range is dragged), so the baseline is empty.
        if (selection.Predicates.Count == 0)
        {
            return new SpanCompare(new SpanSetCondition(new TraceSpanCondition { Name = "A", MinDurationNano = 0 }), query.CompareTopN, from, to);
        }

        var plan = TempoSearchPlanner.Plan(new TraceQlQuery([selection], "A"));
        if (plan.Conditions.Count != 1 || plan.RootServices.Count > 0 || plan.RootNames.Count > 0 || plan.MinTraceDurationNano is not null || plan.MaxTraceDurationNano is not null)
        {
            throw new TraceQlException("compare() needs a selection of spans, e.g. compare({ status = error }); rootName, rootServiceName and traceDuration cannot select spans.");
        }

        return new SpanCompare(new SpanSetCondition(plan.Conditions[0]), query.CompareTopN, from, to);
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
