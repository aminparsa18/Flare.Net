using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Tempo;

/// <summary>
/// A TraceQL metrics query in Flare terms: the span predicate, what to compute per step, and how to group (ADR-0221).
/// </summary>
/// <param name="Condition">The spanset's predicate; null matches every span.</param>
/// <param name="FunctionName">The TraceQL function's name, <c>rate</c> for example.</param>
/// <param name="IsRate">True for <c>rate()</c>: the count per step is divided by the step's seconds.</param>
public sealed record TempoMetricsPlan(
    TraceSpanCondition? Condition,
    SpanMetricFunction Function,
    string FunctionName,
    bool IsRate,
    IReadOnlyList<double> Quantiles,
    IReadOnlyList<SpanGroupBy> GroupBy);

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
            _ => SpanMetricFunction.Sum,
        };

        return new TempoMetricsPlan(
            search.Conditions.FirstOrDefault(),
            function,
            query.FunctionName,
            query.Function == TraceQlMetricsFunction.Rate,
            query.Quantiles,
            query.By.Select(GroupBy).ToArray());
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

        if (field.StartsWith("span.", StringComparison.Ordinal))
        {
            return new SpanGroupBy(field, SpanGroupKind.Attribute, SpanAttributeBag.Span, field["span.".Length..]);
        }

        if (field.StartsWith("resource.", StringComparison.Ordinal))
        {
            return new SpanGroupBy(field, SpanGroupKind.Attribute, SpanAttributeBag.Resource, field["resource.".Length..]);
        }

        // ".x": the same span-or-resource guess a predicate makes.
        var key = field[1..];
        var bag = TempoSearchPlanner.ResourceNamespaces.Any(p => key.StartsWith(p, StringComparison.Ordinal)) ? SpanAttributeBag.Resource : SpanAttributeBag.Span;
        return new SpanGroupBy(field, SpanGroupKind.Attribute, bag, key);
    }
}
