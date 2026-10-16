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

    /// <summary><c>compare()</c> (ADR-0226): built by <see cref="TraceQlMetricsQueryBuilder.BuildCompare"/>, read as <see cref="SpanCompareRow"/>s.</summary>
    Compare,
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

/// <summary>
/// The left side of <c>{ a } &gt;&gt; { b }</c> (ADR-0225, ADR-0231): the counted spans must have a span matching <see cref="Condition"/> related to them as
/// <see cref="Kind"/> says - as their parent, as any ancestor, or as a sibling - or, with <see cref="Negated"/>, have no such span (a trace with
/// no span matching <see cref="Condition"/> then counts all its spans matching the right-hand condition, as Tempo's <c>!&gt;</c>, <c>!&gt;&gt;</c> and <c>!~</c>).
/// </summary>
public sealed record SpanAncestor(TraceSpanCondition Condition, TraceRelationKind Kind, bool Negated = false)
{
    /// <summary>As the original two forms: the parent when <paramref name="Direct"/>, else any ancestor.</summary>
    public SpanAncestor(TraceSpanCondition condition, bool Direct)
        : this(condition, Direct ? TraceRelationKind.Child : TraceRelationKind.Descendant)
    {
    }

    /// <summary>True for a direct parent (<see cref="TraceRelationKind.Child"/>).</summary>
    public bool Direct => Kind == TraceRelationKind.Child;
}

/// <summary>
/// What a query says about the trace's root span (<c>rootServiceName</c>, <c>rootName</c>, <c>traceDuration</c>, ADR-0225): the counted
/// spans must belong to a trace whose root row (the span with no parent) matches all of it. Services and names are alternatives within their list.
/// </summary>
public sealed record SpanTraceRoot(IReadOnlyList<string> Services, IReadOnlyList<string> Names, ulong? MinDurationNano, ulong? MaxDurationNano);

/// <summary>
/// The arguments of <c>compare({ selection }, topN, start, end)</c> (ADR-0226): the spans matching <see cref="Selection"/>
/// (and, with <see cref="From"/> and <see cref="To"/>, that started after the first and at or before the second) are the
/// selection, the other spans of the query the baseline. Per attribute, the <see cref="TopN"/> most common values of each.
/// </summary>
public sealed record SpanCompare(TraceSpanCondition Selection, int TopN, DateTimeOffset? From, DateTimeOffset? To);

/// <summary>One attribute value's span count in one step, for the selection or the baseline (ADR-0226).</summary>
/// <param name="Bucket">The step's index from <see cref="SpanMetricsRequest.From"/>.</param>
/// <param name="Attribute">As TraceQL names it: <c>name</c>, <c>status</c>, <c>kind</c>, <c>span.x</c> or <c>resource.x</c>.</param>
/// <param name="Value">The attribute's value; empty for the spans whose value is not among the top ones, which only count towards the attribute's total.</param>
public sealed record SpanCompareRow(long Bucket, bool Selection, string Attribute, string Value, double Weight);

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
    int Exemplars = 0,
    SpanAncestor? Ancestor = null,
    SpanTraceRoot? Root = null,
    SpanCompare? Compare = null);

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
/// <para>
/// <b>Relations to other spans (ADR-0225).</b> A structural query counts the right-hand spans that have a matching ancestor. A direct
/// parent is a semi-join on <c>(TraceId, ParentSpanId)</c>. An ancestor at any depth is found per trace, as the structure builder's
/// <c>=&gt;</c> is (ADR-0069): the traces with both a left and a right span are collected, each of their spans' parent chains is
/// walked up to <see cref="TraceStructureSqlBuilder.MaxWalkDepth"/> steps, and the right-hand spans whose chain meets a left span are
/// the counted ones. A trace-level restriction is a <c>TraceId</c> lookup of the traces whose root row matches. Both look at spans
/// that started up to <see cref="Lookback"/> before the window, since the ancestor and the root come before the span.
/// </para>
/// </remarks>
public static class TraceQlMetricsQueryBuilder
{
    /// <summary>How long before the window an ancestor or a trace's root may have started and still count (the same slack as the service map's parent side, ADR-0043).</summary>
    public static readonly TimeSpan Lookback = ServiceDependencyQueryBuilder.ParentStartSlack;

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

    /// <summary>The longest value of an attribute <c>compare()</c> looks at; a longer one (a URL with a query string, a stack trace) is not a category.</summary>
    public const int MaxCompareValueLength = 256;

    /// <summary>The most steps a <c>compare()</c> spans: it returns a series per attribute value and step.</summary>
    public const int MaxCompareSteps = 200;

    /// <summary>The most rows a <c>compare()</c> can return; more is an error, never a silent cut.</summary>
    public const int MaxCompareRows = 500_000;

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
    /// <c>compare()</c> (ADR-0226), in two passes over the spans of the query. The first finds, per attribute and per group
    /// (selection or baseline), the <see cref="SpanCompare.TopN"/> + 1 most common values; the extra one lets the caller see that
    /// an attribute has more values than it keeps. The second counts spans per step, group, attribute and value, with every
    /// value outside the first pass's list read as empty, so the rows still add up to the attribute's total per step.
    /// An attribute is every <c>span.x</c> and <c>resource.x</c> the span carries and its <c>name</c>, <c>status</c> and <c>kind</c>;
    /// an empty value, or one longer than <see cref="MaxCompareValueLength"/>, is not counted.
    /// </summary>
    public static SpanMetricsSql BuildCompare(SpanMetricsRequest request, PromotedAttributeColumns? promoted = null)
    {
        var compare = request.Compare ?? throw new ArgumentException("compare() needs its arguments.", nameof(request));
        if ((request.To - request.From).TotalSeconds / request.StepSeconds > MaxCompareSteps)
        {
            throw new ArgumentException($"compare() spans at most {MaxCompareSteps} steps; use a larger step or a shorter range.", nameof(request));
        }

        var (parameters, _, _, _, filter) = Prepare(request, promoted);
        var selected = TraceStructureSqlBuilder.ConditionMatch(compare.Selection, 'S', parameters, promoted);
        if (compare.From is { } from && compare.To is { } to)
        {
            parameters.AddParameter("mSelFrom", from.UtcDateTime);
            parameters.AddParameter("mSelTo", to.UtcDateTime);
            selected += " AND StartTime > {mSelFrom:DateTime64(9)} AND StartTime <= {mSelTo:DateTime64(9)}";
        }

        parameters.AddParameter("mTopN", (long)compare.TopN + 1);
        var attributes =
            "arrayFilter(x -> x.2 != '' AND length(x.2) <= " + MaxCompareValueLength + ", arrayConcat(" +
            "[('name', Name), " +
            "('status', multiIf(StatusCode = 'STATUS_CODE_ERROR', 'error', StatusCode = 'STATUS_CODE_OK', 'ok', 'unset')), " +
            "('kind', ['unspecified', 'internal', 'server', 'client', 'producer', 'consumer'][least(toUInt16(Kind), 5) + 1])], " +
            "arrayMap((k, v) -> (concat('span.', k), v), mapKeys(SpanAttributes), mapValues(SpanAttributes)), " +
            "arrayMap((k, v) -> (concat('resource.', k), v), mapKeys(ResourceAttributes), mapValues(ResourceAttributes))))";
        var spans = "SELECT intDiv(toInt64(toUnixTimestamp(StartTime)) - {mStart:Int64}, {mStep:Int64}) AS Bucket, " +
            $"({selected}) AS IsSel, SampleWeight AS W, {attributes} AS Attrs FROM spans WHERE {filter}";
        // One row per span and attribute value; the pair is split in a subquery because ClickHouse's analyzer cannot reach into the ARRAY JOIN alias from an IN.
        var expanded = $"SELECT Bucket, IsSel, W, p.1 AS A, p.2 AS V FROM ({spans}) ARRAY JOIN Attrs AS p";
        var top = $"SELECT IsSel, A, V FROM ({expanded}) GROUP BY IsSel, A, V ORDER BY sum(W) DESC, V LIMIT {{mTopN:Int64}} BY IsSel, A";

        // The IN is evaluated in its own SELECT, below the GROUP BY: ClickHouse's analyzer loses the column when it is inside the grouped expression.
        var kept = $"SELECT Bucket, IsSel, W, A, if((IsSel, A, V) GLOBAL IN ({top}), V, '') AS Kept FROM ({expanded})";
        var sql = $"SELECT Bucket, IsSel, A, Kept, toFloat64(sum(W)) AS Weight\nFROM ({kept})\n" +
            "GROUP BY Bucket, IsSel, A, Kept\nORDER BY Bucket, IsSel, A, Kept";
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
        parameters.AddParameter("mLookback", (request.From - Lookback).UtcDateTime);

        var groups = request.GroupBy.Select((g, i) => GroupExpression(g, i, parameters)).ToArray();
        var clauses = new List<string> { "StartTime >= {mFrom:DateTime64(9)} AND StartTime < {mTo:DateTime64(9)}" };
        if (request.Condition is { } condition)
        {
            clauses.Add(TraceStructureSqlBuilder.ConditionMatch(condition, 'A', parameters, promoted));
        }

        if (request.Root is { } root)
        {
            clauses.Add(RootClause(root, parameters));
        }

        if (request.Ancestor is { } ancestor)
        {
            clauses.Add(request.Condition is { } span
                ? AncestorClause(ancestor, span, parameters, promoted)
                : throw new ArgumentException("A relation to an ancestor needs a span condition for the counted spans.", nameof(request)));
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

    /// <summary>The window widened backwards by <see cref="Lookback"/>, where ancestors and roots are looked for.</summary>
    private const string LookbackWindow = "StartTime >= {mLookback:DateTime64(9)} AND StartTime < {mTo:DateTime64(9)}";

    /// <summary>The spans of traces whose root row matches (ADR-0225): <c>TraceId GLOBAL IN</c> the roots that do.</summary>
    private static string RootClause(SpanTraceRoot root, ClickHouseParameterCollection parameters)
    {
        var clauses = new List<string> { LookbackWindow, "ParentSpanId = ''" };
        if (root.Services.Count > 0)
        {
            parameters.AddParameter("mrServices", root.Services.ToArray());
            clauses.Add("ServiceName IN {mrServices:Array(String)}");
        }

        if (root.Names.Count > 0)
        {
            parameters.AddParameter("mrNames", root.Names.ToArray());
            clauses.Add("Name IN {mrNames:Array(String)}");
        }

        if (root.MinDurationNano is { } min)
        {
            parameters.AddParameter("mrMin", min);
            clauses.Add("DurationNano >= {mrMin:UInt64}");
        }

        if (root.MaxDurationNano is { } max)
        {
            parameters.AddParameter("mrMax", max);
            clauses.Add("DurationNano <= {mrMax:UInt64}");
        }

        return $"TraceId GLOBAL IN (SELECT TraceId FROM spans WHERE {string.Join(" AND ", clauses)}{ServiceScope.Suffix(parameters)})";
    }

    /// <summary>
    /// The counted spans that stand in <paramref name="ancestor"/>'s relation to a span matching its condition (ADR-0225, ADR-0231), as a restriction
    /// on the span: a parent or ancestor above it, a sibling beside it, or, negated, none of them.
    /// </summary>
    private static string AncestorClause(SpanAncestor ancestor, TraceSpanCondition span, ClickHouseParameterCollection parameters, PromotedAttributeColumns? promoted)
    {
        var above = TraceStructureSqlBuilder.ConditionMatch(ancestor.Condition, 'P', parameters, promoted);
        var scope = ServiceScope.Suffix(parameters);
        var inside = ancestor.Negated ? "GLOBAL NOT IN" : "GLOBAL IN";
        switch (ancestor.Kind)
        {
            case TraceRelationKind.Child:
                return $"(TraceId, ParentSpanId) {inside} (SELECT TraceId, SpanId FROM spans WHERE {LookbackWindow} AND {above}{scope})";
            case TraceRelationKind.Sibling:
            {
                // Another span with the same parent: with one matching span under a parent that is the answer for a span that does not itself match,
                // with two for one that does (it is not its own sibling). Two roots of a trace are siblings, as in Tempo.
                var parents = $"SELECT TraceId, ParentSpanId FROM spans WHERE {LookbackWindow} AND {above}{scope}";
                var clause = $"((TraceId, ParentSpanId) GLOBAL IN ({parents} GROUP BY TraceId, ParentSpanId HAVING count() >= 2) " +
                    $"OR (NOT ({above}) AND (TraceId, ParentSpanId) GLOBAL IN ({parents})))";
                return ancestor.Negated ? $"NOT {clause}" : clause;
            }
        }

        // The ancestor walk, one level per step, as the structure builder's descendant test does; a span that is its own match is not its own ancestor.
        var below = TraceStructureSqlBuilder.ConditionMatch(span, 'A', parameters, promoted);
        var spanHash = TraceStructureSqlBuilder.SpanHash;
        var parentHash = TraceStructureSqlBuilder.ParentHash;
        var depth = TraceStructureSqlBuilder.MaxWalkDepth;
        return $"(TraceId, SpanId) {inside} (SELECT TraceId, SpanId FROM (" +
            $"SELECT TraceId, groupArray({spanHash}) AS allS, groupArray({parentHash}) AS allP, " +
            $"groupArrayIf({spanHash}, {above}) AS aboveS, groupArrayIf(SpanId, {below}) AS belowIds, groupArrayIf({parentHash}, {below}) AS belowP " +
            $"FROM spans WHERE {LookbackWindow}{scope} AND TraceId GLOBAL IN (" +
            $"SELECT TraceId FROM spans WHERE {LookbackWindow}{scope} AND ({above} OR {below}) GROUP BY TraceId HAVING countIf({above}) > 0 AND countIf({below}) > 0) " +
            "GROUP BY TraceId) ARRAY JOIN belowIds AS SpanId, belowP AS startP " +
            $"WHERE arrayFold((acc, x) -> (if(acc.1 = 0, toUInt64(0), allP[indexOf(allS, acc.1)]), acc.2 OR has(aboveS, acc.1)), range(least({depth}, length(allS))), (startP, false)).2)";
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
