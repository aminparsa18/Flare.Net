using System.Globalization;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>What a TraceQL metrics query computes per step of matching spans (ADR-0221).</summary>
public enum SpanMetricFunction
{
    /// <summary>The spans' summed <c>SampleWeight</c>; <c>rate()</c> divides it by the step in seconds afterwards.</summary>
    Count,

    /// <summary>Weighted quantiles of the duration, one per <see cref="SpanMetricsRequest.Quantiles"/> entry.</summary>
    Quantile,
    Avg,
    Min,
    Max,
    Sum,
}

/// <summary>What a <see cref="SpanGroupBy"/> reads from a span.</summary>
public enum SpanGroupKind
{
    Name,
    Status,
    Kind,
    Attribute,
}

/// <summary>One <c>by (...)</c> field. <see cref="Label"/> is how the query wrote it, and names the series label.</summary>
public sealed record SpanGroupBy(string Label, SpanGroupKind Kind, SpanAttributeBag Bag = SpanAttributeBag.Span, string Key = "");

/// <param name="Condition">The span predicate of the query's spanset; null matches every span.</param>
/// <param name="From">A multiple of <paramref name="StepSeconds"/> from the unix epoch, as is <paramref name="To"/> (exclusive).</param>
public sealed record SpanMetricsRequest(
    TraceSpanCondition? Condition,
    SpanMetricFunction Function,
    IReadOnlyList<double> Quantiles,
    IReadOnlyList<SpanGroupBy> GroupBy,
    DateTimeOffset From,
    DateTimeOffset To,
    int StepSeconds);

/// <summary>One group's aggregate in one step. <see cref="Values"/> holds one number, or one per quantile.</summary>
/// <param name="Bucket">The step's index from <see cref="SpanMetricsRequest.From"/>.</param>
/// <param name="Group">The <see cref="SpanMetricsRequest.GroupBy"/> values, as strings (status and kind still as ClickHouse stores them).</param>
/// <param name="Weight">The summed <c>SampleWeight</c>, whatever the function: how many spans the row stands for.</param>
public sealed record SpanMetricRow(long Bucket, IReadOnlyList<string> Group, double Weight, IReadOnlyList<double> Values);

/// <summary>Fully-built <c>SELECT</c> for <see cref="ISpanQueryService.GetSpanMetricsAsync"/>.</summary>
public sealed record SpanMetricsSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="SpanMetricsRequest"/> → parameterized SQL: the spans matching a spanset, grouped into steps
/// (<c>intDiv(unix seconds - from, step)</c>) and by the <c>by (...)</c> fields, aggregated in ClickHouse. The
/// spanset's predicate is <see cref="TraceStructureSqlBuilder.ConditionMatch"/>, the one the Tempo search uses.
/// </summary>
/// <remarks>
/// <para>
/// Every count, sum and quantile weights a row by <c>SampleWeight</c> (ADR-0122), so a sampled service reports
/// its true rate. With a <c>by</c> clause the groups are first ranked by span count in a subquery, and only the
/// top <see cref="MaxSeries"/> + 1 are aggregated: the caller sees one more than it will keep and so knows the
/// answer was cut, rather than the database cutting it silently.
/// </para>
/// <para>
/// A span lacking a grouped attribute is left out (a <c>mapContains</c> in the filter), not grouped under an
/// empty value. An attribute with old and new semantic-convention spellings reads whichever the span has.
/// </para>
/// </remarks>
public static class TraceQlMetricsQueryBuilder
{
    /// <summary>The most series a query returns.</summary>
    public const int MaxSeries = 200;

    /// <summary>The most steps a query spans.</summary>
    public const int MaxSteps = 1500;

    /// <summary>The most rows a query can produce: every step of every series, plus the one extra series that shows the cut.</summary>
    public const int MaxRows = MaxSteps * (MaxSeries + 1);

    public static SpanMetricsSql Build(SpanMetricsRequest request, PromotedAttributeColumns? promoted = null)
    {
        if (request.StepSeconds < 1)
        {
            throw new ArgumentException("The step must be at least one second.", nameof(request));
        }

        if (request.To <= request.From || (request.To - request.From).TotalSeconds / request.StepSeconds > MaxSteps)
        {
            throw new ArgumentException($"A metrics query spans at most {MaxSteps} steps.", nameof(request));
        }

        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("mFrom", request.From.UtcDateTime);
        parameters.AddParameter("mTo", request.To.UtcDateTime);
        parameters.AddParameter("mStart", request.From.ToUnixTimeSeconds());
        parameters.AddParameter("mStep", (long)request.StepSeconds);

        var groups = request.GroupBy.Select((g, i) => GroupExpression(g, i, parameters)).ToArray();
        var clauses = new List<string> { "StartTime >= {mFrom:DateTime64(9)} AND StartTime < {mTo:DateTime64(9)}" };
        if (request.Condition is { } condition)
        {
            clauses.Add(TraceStructureSqlBuilder.ConditionMatch(condition, 'A', parameters, promoted));
        }

        clauses.AddRange(groups.Select(g => g.Present));
        var where = string.Join(" AND ", clauses) + ServiceScope.Suffix(parameters);

        var select = new List<string>
        {
            "intDiv(toInt64(toUnixTimestamp(StartTime)) - {mStart:Int64}, {mStep:Int64}) AS Bucket",
        };
        select.AddRange(groups.Select((g, i) => $"{g.Value} AS G{i}"));
        select.Add("toFloat64(sum(SampleWeight)) AS Weight");
        select.AddRange(Aggregates(request));

        var filter = where;
        if (groups.Length > 0)
        {
            var expressions = string.Join(", ", groups.Select(g => g.Value));
            var tuple = groups.Length == 1 ? expressions : $"({expressions})";
            filter += $" AND {tuple} GLOBAL IN (SELECT {expressions} FROM spans WHERE {where} GROUP BY {expressions} " +
                $"ORDER BY sum(SampleWeight) DESC, {expressions} LIMIT {MaxSeries + 1})";
        }

        var groupBy = new[] { "Bucket" }.Concat(groups.Select((_, i) => $"G{i}"));
        var sql = $"SELECT {string.Join(", ", select)}\nFROM spans WHERE {filter}\nGROUP BY {string.Join(", ", groupBy)}\nORDER BY {string.Join(", ", groupBy)}";
        return new SpanMetricsSql(sql, parameters);
    }

    private static IEnumerable<string> Aggregates(SpanMetricsRequest request)
    {
        switch (request.Function)
        {
            case SpanMetricFunction.Count:
                yield break;
            case SpanMetricFunction.Quantile:
                for (var i = 0; i < request.Quantiles.Count; i++)
                {
                    var q = request.Quantiles[i];
                    if (!double.IsFinite(q) || q < 0 || q > 1)
                    {
                        throw new ArgumentException($"Quantile {q} is outside 0 to 1.", nameof(request));
                    }

                    // The level is a validated number, formatted invariantly: ClickHouse takes it as a function parameter, not a bind parameter.
                    yield return $"quantileTDigestWeighted({q.ToString("R", CultureInfo.InvariantCulture)})(DurationNano, SampleWeight) AS V{i}";
                }

                break;
            case SpanMetricFunction.Avg:
                yield return "sum(toFloat64(DurationNano) * SampleWeight) / sum(SampleWeight) AS V0";
                break;
            case SpanMetricFunction.Min:
                yield return "toFloat64(min(DurationNano)) AS V0";
                break;
            case SpanMetricFunction.Max:
                yield return "toFloat64(max(DurationNano)) AS V0";
                break;
            default:
                yield return "sum(toFloat64(DurationNano) * SampleWeight) AS V0";
                break;
        }
    }

    /// <summary>A group field's value expression (always a string) and the clause that holds for a span that has it.</summary>
    private static (string Value, string Present) GroupExpression(SpanGroupBy group, int index, ClickHouseParameterCollection parameters)
    {
        switch (group.Kind)
        {
            case SpanGroupKind.Name:
                return ("Name", "1");
            case SpanGroupKind.Status:
                return ("toString(StatusCode)", "1");
            case SpanGroupKind.Kind:
                return ("toString(Kind)", "1");
        }

        var column = SpanFilterSqlBuilder.ColumnFor(group.Bag);
        var keys = group.Bag == SpanAttributeBag.Span && SemconvAliases.Group(group.Key) is { } aliases ? aliases : [group.Key];
        var parts = new List<string>();
        var contains = new List<string>();
        for (var k = 0; k < keys.Count; k++)
        {
            var name = $"mgKey{index}x{k}";
            parameters.AddParameter(name, keys[k]);
            contains.Add($"mapContains({column}, {{{name}:String}})");
            parts.Add($"{contains[k]}, {column}[{{{name}:String}}]");
        }

        var present = contains.Count == 1 ? contains[0] : $"({string.Join(" OR ", contains)})";
        var value = keys.Count == 1 ? $"{column}[{{mgKey{index}x0:String}}]" : $"multiIf({string.Join(", ", parts)}, '')";
        return (value, present);
    }
}
