using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Tempo;

/// <summary>
/// A TraceQL metrics query in Flare terms: the span predicate, what to compute per step, and how to group (ADR-0221).
/// </summary>
/// <param name="Condition">The spanset's predicate; null matches every span.</param>
/// <param name="FunctionName">The TraceQL function's name, <c>rate</c> for example.</param>
/// <param name="IsRate">True for <c>rate()</c>: the count per step is divided by the step's seconds.</param>
/// <param name="Rank">The <c>| topk(n)</c> / <c>| bottomk(n)</c> stage, applied to the series after they are computed (ADR-0224).</param>
/// <param name="Exemplars">Whether the query itself asked for exemplars; a request can also ask with <c>exemplars=n</c> (ADR-0223).</param>
/// <param name="Value">The numeric attribute a value function aggregates (ADR-0222); null is the span's duration, which is reported in seconds.</param>
public sealed record TempoMetricsPlan(
    TraceSpanCondition? Condition,
    SpanMetricFunction Function,
    string FunctionName,
    bool IsRate,
    IReadOnlyList<double> Quantiles,
    IReadOnlyList<SpanGroupBy> GroupBy,
    SpanValueAttribute? Value = null,
    TraceQlRank? Rank = null,
    bool Exemplars = false);

/// <summary>Compiles a parsed <see cref="TraceQlMetricsQuery"/> into a <see cref="TempoMetricsPlan"/>. Pure, no ClickHouse dependency.</summary>
/// <remarks>
/// The spanset goes through <see cref="TempoSearchPlanner"/>, so a metrics query and a search read a predicate the
/// same way (anchored regular expressions, the unscoped-attribute guess). Trace-level fields filter a trace's root
/// row in a search and have no per-span meaning here, so they are rejected.
/// </remarks>
public static class TempoMetricsPlanner
{
    public static TempoMetricsPlan Plan(TraceQlMetricsQuery query)
    {
        var search = TempoSearchPlanner.Plan(new TraceQlQuery([query.Spanset], "A"));
        if (search.RootServices.Count > 0 || search.RootNames.Count > 0
            || search.MinTraceDurationNano is not null || search.MaxTraceDurationNano is not null)
        {
            throw new TraceQlException("rootName, rootServiceName and traceDuration describe the whole trace, so they cannot be used in a metrics query.");
        }

        var function = query.Function switch
        {
            TraceQlMetricsFunction.Rate or TraceQlMetricsFunction.CountOverTime => SpanMetricFunction.Count,
            TraceQlMetricsFunction.QuantileOverTime => SpanMetricFunction.Quantile,
            TraceQlMetricsFunction.AvgOverTime => SpanMetricFunction.Avg,
            TraceQlMetricsFunction.MinOverTime => SpanMetricFunction.Min,
            TraceQlMetricsFunction.MaxOverTime => SpanMetricFunction.Max,
            TraceQlMetricsFunction.HistogramOverTime => SpanMetricFunction.Histogram,
            _ => SpanMetricFunction.Sum,
        };

        return new TempoMetricsPlan(
            search.Conditions.FirstOrDefault(),
            function,
            query.FunctionName,
            query.Function == TraceQlMetricsFunction.Rate,
            query.Quantiles,
            query.By.Select(GroupBy).ToArray(),
            query.ValueField is { } value ? ValueAttribute(value) : null,
            query.Rank,
            query.Exemplars);
    }

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
