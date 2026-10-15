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

    /// <summary>
    /// <c>histogram_over_time</c> (ADR-0223): the weighted span count per step, group and power-of-two bucket of the value.
    /// A row's <see cref="SpanMetricRow.Values"/> holds the bucket's exponent <c>k</c>, the bucket being values up to <c>2^k</c>.
    /// </summary>
    Histogram,
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

/// <summary>A numeric span or resource attribute a metric aggregates in place of the duration (ADR-0222). <see cref="Label"/> is how the query wrote it.</summary>
public sealed record SpanValueAttribute(string Label, SpanAttributeBag Bag, string Key);

/// <param name="Condition">The span predicate of the query's spanset; null matches every span.</param>
/// <param name="Exemplars">With <see cref="TraceQlMetricsQueryBuilder.BuildExemplars"/>: the most exemplars wanted; ignored by <see cref="TraceQlMetricsQueryBuilder.Build"/>.</param>
/// <param name="Value">What <see cref="Function"/> aggregates; null is the span's duration. Ignored by <see cref="SpanMetricFunction.Count"/>.</param>
/// <param name="From">A multiple of <paramref name="StepSeconds"/> from the unix epoch, as is <paramref name="To"/> (exclusive).</param>
public sealed record SpanMetricsRequest(
    TraceSpanCondition? Condition,
    SpanMetricFunction Function,
    IReadOnlyList<double> Quantiles,
    IReadOnlyList<SpanGroupBy> GroupBy,
    DateTimeOffset From,
    DateTimeOffset To,
    int StepSeconds,
    SpanValueAttribute? Value = null,
    int Exemplars = 0);

/// <summary>One group's aggregate in one step. <see cref="Values"/> holds one number, or one per quantile.</summary>
/// <param name="Bucket">The step's index from <see cref="SpanMetricsRequest.From"/>.</param>
/// <param name="Group">The <see cref="SpanMetricsRequest.GroupBy"/> values, as strings (status and kind still as ClickHouse stores them).</param>
/// <param name="Weight">The summed <c>SampleWeight</c>, whatever the function: how many spans the row stands for.</param>
public sealed record SpanMetricRow(long Bucket, IReadOnlyList<string> Group, double Weight, IReadOnlyList<double> Values);

/// <summary>The span standing out in one step of one group: the one with the largest value, as a link into a trace (ADR-0223).</summary>
/// <param name="Bucket">The step's index from <see cref="SpanMetricsRequest.From"/>.</param>
/// <param name="Group">The <see cref="SpanMetricsRequest.GroupBy"/> values, as in <see cref="SpanMetricRow"/>.</param>
/// <param name="Value">The span's duration in nanoseconds, or the attribute's number, as the query aggregates.</param>
public sealed record SpanExemplarRow(long Bucket, IReadOnlyList<string> Group, string TraceId, string SpanId, double Value, long TimestampMs);

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
/// <para>
/// A function over an attribute (ADR-0222) reads it as <c>toFloat64OrNull</c> of the stored string and leaves out
/// spans where it is absent, does not parse or is not finite, the same way: those spans take no part in the sums,
/// the quantiles or the ranking of the top series, and the <c>Weight</c> counts only the spans that had a number.
/// </para>
/// </remarks>
public static class TraceQlMetricsQueryBuilder
{
    /// <summary>The most series a query returns.</summary>
    public const int MaxSeries = 200;

    /// <summary>The most groups a <c>histogram_over_time</c> keeps: each is a series per bucket, up to <see cref="HistogramBuckets"/>.</summary>
    public const int MaxHistogramGroups = 20;

    /// <summary>The most power-of-two buckets: <c>2^1</c> to <c>2^63</c>.</summary>
    public const int HistogramBuckets = 63;

    /// <summary>The most exemplars a query returns, whatever it asks for.</summary>
    public const int MaxExemplars = 1000;

    /// <summary>The most groups (series, for the other functions) a query keeps.</summary>
    public static int MaxGroups(SpanMetricFunction function) => function == SpanMetricFunction.Histogram ? MaxHistogramGroups : MaxSeries;

    /// <summary>The most steps a query spans.</summary>
    public const int MaxSteps = 1500;

    /// <summary>The most rows a query can produce: every step of every series, plus the one extra series that shows the cut.</summary>
    public const int MaxRows = MaxSteps * (MaxSeries + 1);

    /// <summary>The most rows a <c>histogram_over_time</c> can produce: every step of every bucket of every group, plus the one extra group.</summary>
    public const int MaxHistogramRows = MaxSteps * (MaxHistogramGroups + 1) * HistogramBuckets;

    public static int MaxRowsFor(SpanMetricFunction function) => function == SpanMetricFunction.Histogram ? MaxHistogramRows : MaxRows;

    public static SpanMetricsSql Build(SpanMetricsRequest request, PromotedAttributeColumns? promoted = null)
    {
        var (parameters, groups, value, where, filter) = Prepare(request, promoted);
        var histogram = request.Function == SpanMetricFunction.Histogram;

        var select = new List<string>
        {
            "intDiv(toInt64(toUnixTimestamp(StartTime)) - {mStart:Int64}, {mStep:Int64}) AS Bucket",
        };
        select.AddRange(groups.Select((g, i) => $"{g.Value} AS G{i}"));
        select.Add("toFloat64(sum(SampleWeight)) AS Weight");
        select.AddRange(Aggregates(request, value));

        var groupBy = new[] { "Bucket" }.Concat(groups.Select((_, i) => $"G{i}")).ToList();
        if (histogram)
        {
            groupBy.Add("V0");
        }

        var sql = $"SELECT {string.Join(", ", select)}\nFROM spans WHERE {filter}\nGROUP BY {string.Join(", ", groupBy)}\nORDER BY {string.Join(", ", groupBy)}";
        return new SpanMetricsSql(sql, parameters);
    }

    /// <summary>
    /// The span with the largest value in each step of each group, the largest <see cref="SpanMetricsRequest.Exemplars"/>
    /// (at most <see cref="MaxExemplars"/>) of them: one more read of the same spans, for a link from a point on a chart to a trace.
    /// </summary>
    public static SpanMetricsSql BuildExemplars(SpanMetricsRequest request, PromotedAttributeColumns? promoted = null)
    {
        var (parameters, groups, value, _, filter) = Prepare(request, promoted);
        var limit = Math.Clamp(request.Exemplars, 1, MaxExemplars);

        var select = new List<string>
        {
            "intDiv(toInt64(toUnixTimestamp(StartTime)) - {mStart:Int64}, {mStep:Int64}) AS Bucket",
        };
        select.AddRange(groups.Select((g, i) => $"{g.Value} AS G{i}"));
        select.Add($"argMax(TraceId, {value}) AS ExTrace");
        select.Add($"argMax(SpanId, {value}) AS ExSpan");
        select.Add($"toFloat64(max({value})) AS ExValue");
        select.Add($"toInt64(toUnixTimestamp64Milli(argMax(StartTime, {value}))) AS ExTime");

        var groupBy = new[] { "Bucket" }.Concat(groups.Select((_, i) => $"G{i}"));
        var sql = $"SELECT {string.Join(", ", select)}\nFROM spans WHERE {filter}\nGROUP BY {string.Join(", ", groupBy)}\nORDER BY ExValue DESC, Bucket, {string.Join(", ", groups.Select((_, i) => $"G{i}").Append("ExTrace"))}\nLIMIT {limit}";
        return new SpanMetricsSql(sql, parameters);
    }

    /// <summary>The parts <see cref="Build"/> and <see cref="BuildExemplars"/> share: the filter, the group expressions and the value expression.</summary>
    /// <returns><c>Value</c> is the expression the function aggregates (<c>DurationNano</c> or the attribute's number); <c>Filter</c> is <c>Where</c> plus, with groups, the restriction to the top ones.</returns>
    private static (ClickHouseParameterCollection Parameters, (string Value, string Present)[] Groups, string Value, string Where, string Filter) Prepare(
        SpanMetricsRequest request, PromotedAttributeColumns? promoted)
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

        // Count (rate, count_over_time) takes no value; the histogram and the exemplars do, so for them the attribute is not optional.
        var value = "DurationNano";
        if (request.Value is { } attribute && request.Function != SpanMetricFunction.Count)
        {
            var (number, present) = NumericExpression(attribute, parameters);
            clauses.Add(present);
            value = $"assumeNotNull({number})";
        }

        if (request.Function == SpanMetricFunction.Histogram)
        {
            // Tempo drops what Log2Bucketize returns -1 for (below 2) and what no uint64 holds.
            clauses.Add($"{value} >= 2 AND {value} < 9.2e18");
        }

        var where = string.Join(" AND ", clauses) + ServiceScope.Suffix(parameters);
        var filter = where;
        if (groups.Length > 0)
        {
            var expressions = string.Join(", ", groups.Select(g => g.Value));
            var tuple = groups.Length == 1 ? expressions : $"({expressions})";
            filter += $" AND {tuple} GLOBAL IN (SELECT {expressions} FROM spans WHERE {where} GROUP BY {expressions} " +
                $"ORDER BY sum(SampleWeight) DESC, {expressions} LIMIT {MaxGroups(request.Function) + 1})";
        }

        return (parameters, groups, value, where, filter);
    }

    private static IEnumerable<string> Aggregates(SpanMetricsRequest request, string value)
    {
        switch (request.Function)
        {
            case SpanMetricFunction.Count:
                yield break;
            case SpanMetricFunction.Histogram:
                // The smallest k with 2^k >= the value, as Tempo's Log2Bucketize does on the value truncated to an integer.
                yield return $"toUInt8(ceil(log2(toFloat64(toUInt64({value}))))) AS V0";
                break;
            case SpanMetricFunction.Quantile:
                for (var i = 0; i < request.Quantiles.Count; i++)
                {
                    var q = request.Quantiles[i];
                    if (!double.IsFinite(q) || q < 0 || q > 1)
                    {
                        throw new ArgumentException($"Quantile {q} is outside 0 to 1.", nameof(request));
                    }

                    // The level is a validated number, formatted invariantly: ClickHouse takes it as a function parameter, not a bind parameter.
                    yield return $"quantileTDigestWeighted({q.ToString("R", CultureInfo.InvariantCulture)})({value}, SampleWeight) AS V{i}";
                }

                break;
            case SpanMetricFunction.Avg:
                yield return $"sum(toFloat64({value}) * SampleWeight) / sum(SampleWeight) AS V0";
                break;
            case SpanMetricFunction.Min:
                yield return $"toFloat64(min({value})) AS V0";
                break;
            case SpanMetricFunction.Max:
                yield return $"toFloat64(max({value})) AS V0";
                break;
            default:
                yield return $"sum(toFloat64({value}) * SampleWeight) AS V0";
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

        var (value, present) = AttributeText(group.Bag, group.Key, $"mgKey{index}x", parameters);
        return (value, present);
    }

    /// <summary>
    /// The attribute's value as a number, <c>NULL</c> when it does not parse, and the clause that holds only for a span
    /// whose value is a finite number (so the unparseable ones are left out, as are the spans without the attribute).
    /// </summary>
    private static (string Number, string Present) NumericExpression(SpanValueAttribute attribute, ClickHouseParameterCollection parameters)
    {
        var column = SpanFilterSqlBuilder.ColumnFor(attribute.Bag);
        var keys = KeysOf(attribute.Bag, attribute.Key);
        var names = keys.Select((key, k) =>
        {
            var name = $"mvKey{k}";
            parameters.AddParameter(name, key);
            return name;
        }).ToArray();

        var number = names.Length == 1
            ? $"toFloat64OrNull({column}[{{{names[0]}:String}}])"
            : $"multiIf({string.Join(", ", names.Select(n => $"mapContains({column}, {{{n}:String}}), toFloat64OrNull({column}[{{{n}:String}}])"))}, NULL)";
        return (number, $"ifNull(isFinite({number}), 0)");
    }

    private static IReadOnlyList<string> KeysOf(SpanAttributeBag bag, string key) =>
        bag == SpanAttributeBag.Span && SemconvAliases.Group(key) is { } aliases ? aliases : [key];

    private static (string Value, string Present) AttributeText(SpanAttributeBag bag, string key, string namePrefix, ClickHouseParameterCollection parameters)
    {
        var column = SpanFilterSqlBuilder.ColumnFor(bag);
        var keys = KeysOf(bag, key);
        var parts = new List<string>();
        var contains = new List<string>();
        for (var k = 0; k < keys.Count; k++)
        {
            var name = $"{namePrefix}{k}";
            parameters.AddParameter(name, keys[k]);
            contains.Add($"mapContains({column}, {{{name}:String}})");
            parts.Add($"{contains[k]}, {column}[{{{name}:String}}]");
        }

        var present = contains.Count == 1 ? contains[0] : $"({string.Join(" OR ", contains)})";
        var value = keys.Count == 1 ? $"{column}[{{{namePrefix}0:String}}]" : $"multiIf({string.Join(", ", parts)}, '')";
        return (value, present);
    }
}
