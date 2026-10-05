using System.Globalization;
using System.Text;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT</c> for a trace funnel query, ready to hand to <see cref="TraceFunnelQueryService"/>.</summary>
public sealed record TraceFunnelSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure SQL builder for trace funnels (<c>POST /api/traces/funnel</c> and its
/// <c>/traces</c> drill-down): how many traces in a window pass through an ordered list of
/// steps, where each step is a span match. Computed from <c>spans</c> at query time - no
/// new table. See docs-internal/adr/0067-trace-funnels.md.
/// </summary>
/// <remarks>
/// <para>
/// <b>Semantics.</b> A trace enters the funnel at its earliest span matching step 1. Each
/// later step then takes the earliest span matching it that starts no earlier than the
/// previous step's span and isn't that same span - a greedy, in-order walk, so a step
/// matched only before the previous one (a retry that happened first, say) doesn't count,
/// but a later match of it does. A trace's level is how far that walk gets. Only spans that
/// start inside the window take part, so a trace straddling the window's end can look like
/// it dropped off.
/// </para>
/// <para>
/// <b>Shape.</b> The innermost query reads only spans matching some step (plus a
/// <c>ServiceName IN</c> prefilter when every step names a service, so <c>idx_service</c>
/// can skip granules), and groups them by trace into one sorted
/// <c>(StartTime, cityHash64(SpanId), isError)</c> array per step. The middle query walks
/// those arrays with <c>arrayFirstIndex</c> into <c>i1..iN</c> - the matched element's index
/// per step, 0 when the walk stopped before it. The outer query aggregates (summary) or
/// filters and lists (drill-down). The span id is hashed only to keep the per-trace arrays
/// small; it is compared, never shown.
/// </para>
/// <para>
/// <b>Cost.</b> One pass over the window's matching spans, holding one small array set per
/// entering trace. The spike (ADR-0067) measured ~1.2 s and ~490 MiB for 2M traces / 12.6M
/// spans in one hour on a single node. It can't use migration 0025's
/// <c>spans_by_start_time</c> projection, which lacks <c>Name</c> and <c>StatusCode</c>;
/// the usual <see cref="QuerySafety"/> caps bound it.
/// </para>
/// </remarks>
public static class TraceFunnelQueryBuilder
{
    public const int DefaultWindowMinutes = 60;
    public const int MinWindowMinutes = 5;
    public const int MaxWindowMinutes = 1440;

    public const int MinSteps = 2;
    public const int MaxSteps = 6;

    /// <summary>Row cap for a drill-down list.</summary>
    public const int MaxTraces = 100;

    public static int ClampWindowMinutes(int? windowMinutes) =>
        windowMinutes is > 0 ? Math.Clamp(windowMinutes.Value, MinWindowMinutes, MaxWindowMinutes) : DefaultWindowMinutes;

    public static DateTimeOffset ResolveWindowEnd(long? endUnixMs, DateTimeOffset now) =>
        endUnixMs is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : now;

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when <paramref name="steps"/> isn't a usable
    /// funnel: too few/many steps, or a step with no condition at all (which would match
    /// every span and make the funnel meaningless).
    /// </summary>
    public static IReadOnlyList<TraceFunnelStep> ValidateSteps(IReadOnlyList<TraceFunnelStep>? steps)
    {
        if (steps is null || steps.Count < MinSteps || steps.Count > MaxSteps)
        {
            throw new ArgumentException($"A funnel needs {MinSteps} to {MaxSteps} steps.", nameof(steps));
        }

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (string.IsNullOrEmpty(step.ServiceName) && string.IsNullOrEmpty(step.SpanName) && step.Attributes is not { Count: > 0 })
            {
                throw new ArgumentException($"Step {i + 1} needs a service, a span name or an attribute filter.", nameof(steps));
            }
        }

        return steps;
    }

    /// <summary>
    /// One row: for each step, in order, <c>TraceCount</c>, <c>ErrorCount</c>, the average
    /// transition (ms, <c>nan</c> when no trace made it) and <c>[p50, p95, p99]</c> transition
    /// (ms). The first step's transition columns are constant zeros.
    /// </summary>
    public static TraceFunnelSql BuildSummary(TraceFunnelRequest request, DateTimeOffset now, PromotedAttributeColumns? promoted = null)
    {
        var steps = ValidateSteps(request.Steps);
        var parameters = new ClickHouseParameterCollection();
        var walk = BuildWalk(steps, request.WindowMinutes, request.EndUnixMs, now, parameters, promoted);

        var columns = new List<string>();
        for (var k = 1; k <= steps.Count; k++)
        {
            columns.Add($"countIf(i{k} > 0)");
            columns.Add($"countIf(i{k} > 0 AND m{k}[i{k}].3)");
            if (k == 1)
            {
                columns.Add("toFloat64(0)");
                columns.Add("[toFloat64(0), 0, 0]");
            }
            else
            {
                columns.Add($"avgIf({TransitionMsExpr(k)}, i{k} > 0)");
                columns.Add($"quantilesIf(0.5, 0.95, 0.99)({TransitionMsExpr(k)}, i{k} > 0)");
            }
        }

        var sql = $"SELECT\n    {string.Join(",\n    ", columns)}\nFROM\n(\n{walk}\n)";
        return new TraceFunnelSql(sql, parameters);
    }

    /// <summary>
    /// Up to <see cref="MaxTraces"/> rows of <c>TraceId</c>, first-step start (Unix ms),
    /// reached step count and elapsed ms, most recent first. Throws
    /// <see cref="ArgumentException"/> for an out-of-range <see cref="TraceFunnelTracesRequest.StepIndex"/>
    /// or <see cref="TraceFunnelOutcome.Dropped"/> on the last step (nothing to drop to).
    /// </summary>
    public static TraceFunnelSql BuildTraces(TraceFunnelTracesRequest request, DateTimeOffset now, PromotedAttributeColumns? promoted = null)
    {
        var steps = ValidateSteps(request.Steps);
        if (request.StepIndex < 0 || request.StepIndex >= steps.Count)
        {
            throw new ArgumentException("stepIndex is out of range.", nameof(request));
        }

        var k = request.StepIndex + 1;
        var condition = request.Outcome switch
        {
            TraceFunnelOutcome.Reached => $"i{k} > 0",
            TraceFunnelOutcome.Errored => $"i{k} > 0 AND m{k}[i{k}].3",
            _ when k == steps.Count => throw new ArgumentException("The last step has no drop-off.", nameof(request)),
            _ => $"i{k} > 0 AND i{k + 1} = 0",
        };

        var parameters = new ClickHouseParameterCollection();
        var walk = BuildWalk(steps, request.WindowMinutes, request.EndUnixMs, now, parameters, promoted);

        var reached = new StringBuilder("1");
        var lastTime = new StringBuilder("multiIf(");
        for (var j = steps.Count; j >= 2; j--)
        {
            lastTime.Append(CultureInfo.InvariantCulture, $"i{j} > 0, m{j}[i{j}].1, ");
        }

        lastTime.Append("m1[1].1)");
        for (var j = 2; j <= steps.Count; j++)
        {
            reached.Append(CultureInfo.InvariantCulture, $" + (i{j} > 0)");
        }

        var sql =
            "SELECT\n" +
            "    TraceId,\n" +
            "    toUnixTimestamp64Milli(m1[1].1) AS startMs,\n" +
            $"    toInt32({reached}) AS reachedSteps,\n" +
            $"    (toUnixTimestamp64Nano({lastTime}) - toUnixTimestamp64Nano(m1[1].1)) / 1e6 AS elapsedMs\n" +
            $"FROM\n(\n{walk}\n)\n" +
            $"WHERE {condition}\n" +
            "ORDER BY startMs DESC, TraceId\n" +
            $"LIMIT {MaxTraces}";
        return new TraceFunnelSql(sql, parameters);
    }

    /// <summary>Transition into step <paramref name="k"/> (≥ 2) in milliseconds: start of its matched span minus start of the previous step's.</summary>
    private static string TransitionMsExpr(int k) =>
        $"(toUnixTimestamp64Nano(m{k}[i{k}].1) - toUnixTimestamp64Nano(m{k - 1}[i{k - 1}].1)) / 1e6";

    /// <summary>The inner + middle queries (see the class remarks): one row per entering trace with <c>m1..mN</c> and <c>i1..iN</c>.</summary>
    private static string BuildWalk(
        IReadOnlyList<TraceFunnelStep> steps,
        int? windowMinutes,
        long? endUnixMs,
        DateTimeOffset now,
        ClickHouseParameterCollection parameters,
        PromotedAttributeColumns? promoted)
    {
        var end = ResolveWindowEnd(endUnixMs, now);
        var start = end.AddMinutes(-ClampWindowMinutes(windowMinutes));
        parameters.AddParameter("from", start.UtcDateTime);
        parameters.AddParameter("to", end.UtcDateTime);

        var matches = new List<string>(steps.Count);
        for (var i = 0; i < steps.Count; i++)
        {
            matches.Add(StepMatch(steps[i], i + 1, parameters, promoted));
        }

        var where = new List<string>
        {
            "StartTime >= {from:DateTime64(9)}",
            "StartTime < {to:DateTime64(9)}",
        };
        if (steps.All(s => !string.IsNullOrEmpty(s.ServiceName)))
        {
            parameters.AddParameter("funnelServices", steps.Select(s => s.ServiceName!).Distinct(StringComparer.Ordinal).ToArray());
            where.Add("ServiceName IN {funnelServices:Array(String)}");
        }

        ServiceScope.Append(where, parameters);

        where.Add($"({string.Join(" OR ", matches)})");

        var arrays = matches.Select((match, i) =>
            $"arraySort(groupArrayIf((StartTime, cityHash64(SpanId), StatusCode = 'STATUS_CODE_ERROR'), {match})) AS m{i + 1}");

        var walk = new List<string> { "TraceId" };
        walk.AddRange(Enumerable.Range(1, steps.Count).Select(k => $"m{k}"));
        walk.Add("1 AS i1");
        for (var k = 2; k <= steps.Count; k++)
        {
            var p = k - 1;
            walk.Add($"if(i{p} = 0, 0, arrayFirstIndex(x -> x.1 >= m{p}[i{p}].1 AND x.2 != m{p}[i{p}].2, m{k})) AS i{k}");
        }

        return
            $"    SELECT {string.Join(", ", walk)}\n" +
            "    FROM\n" +
            "    (\n" +
            $"        SELECT TraceId, {string.Join(", ", arrays)}\n" +
            "        FROM spans\n" +
            $"        WHERE {string.Join(" AND ", where)}\n" +
            "        GROUP BY TraceId\n" +
            "        HAVING length(m1) > 0\n" +
            "    )";
    }

    /// <summary>One step's span condition, with its parameters bound under an <c>s{k}</c> prefix so steps never collide.</summary>
    private static string StepMatch(TraceFunnelStep step, int k, ClickHouseParameterCollection parameters, PromotedAttributeColumns? promoted)
    {
        var clauses = new List<string>();
        if (!string.IsNullOrEmpty(step.ServiceName))
        {
            parameters.AddParameter($"s{k}Service", step.ServiceName);
            clauses.Add($"ServiceName = {{s{k}Service:String}}");
        }

        if (!string.IsNullOrEmpty(step.SpanName))
        {
            parameters.AddParameter($"s{k}Name", step.SpanName);
            clauses.Add($"Name = {{s{k}Name:String}}");
        }

        if (step.Attributes is { Count: > 0 } attributes)
        {
            for (var i = 0; i < attributes.Count; i++)
            {
                clauses.Add(SpanFilterSqlBuilder.AttributeClause(attributes[i], $"S{k}_{i}", parameters, promoted));
            }
        }

        return $"({string.Join(" AND ", clauses)})";
    }
}
