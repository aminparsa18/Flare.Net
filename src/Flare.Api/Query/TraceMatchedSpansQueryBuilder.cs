using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>The spans of <see cref="TraceIds"/> that match a <see cref="TraceStructureFilter"/>'s conditions - the Tempo API's <c>spanSets</c> (ADR-0219).</summary>
public sealed record MatchedSpansRequest(IReadOnlyList<string> TraceIds, TraceStructureFilter Structure, int SpansPerTrace);

/// <summary>One matching span of a trace, as much of it as a Tempo span set shows.</summary>
public sealed record MatchedSpan(string SpanId, string ServiceName, string Name, DateTimeOffset StartTime, ulong DurationNano);

/// <summary>The matching spans of one trace: the first few, and how many there are in all.</summary>
public sealed record TraceMatchedSpans(string TraceId, int Matched, IReadOnlyList<MatchedSpan> Spans);

/// <summary>Fully-built <c>SELECT</c> for <see cref="ISpanQueryService.GetMatchedSpansAsync"/>.</summary>
public sealed record MatchedSpansSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="MatchedSpansRequest"/> → parameterized SQL. A Tempo search lists traces, found by
/// <see cref="TraceStructureSqlBuilder"/> from their root span; this asks, for the traces it returned, which
/// of their spans satisfy the query's conditions, reusing the same per-condition predicate
/// (<see cref="TraceStructureSqlBuilder.ConditionMatch"/>) so both agree on what "matches" means.
/// </summary>
/// <remarks>
/// <para>
/// No time window: the lookup is by <c>TraceId</c>, the first column of the sort key, and a trace's spans can
/// start outside the window the root did. The service scope still applies. <c>count() OVER (PARTITION BY
/// TraceId)</c> gives the total per trace and <c>LIMIT n BY TraceId</c> keeps the first few by start time, so
/// the response stays small however many spans match.
/// </para>
/// <para>
/// For a structural query (<c>{ A } &gt;&gt; { B }</c>) every condition's spans are listed, not only the ones
/// that satisfy the relationship; the trace is in the result because the relationship holds.
/// </para>
/// </remarks>
public static class TraceMatchedSpansQueryBuilder
{
    public const int MaxSpansPerTrace = 100;

    public static MatchedSpansSql Build(MatchedSpansRequest request, PromotedAttributeColumns? promoted = null)
    {
        if (request.TraceIds.Count == 0)
        {
            throw new ArgumentException("At least one trace id is needed.", nameof(request));
        }

        var parameters = new ClickHouseParameterCollection();
        var (_, conditions) = TraceStructureSqlBuilder.Validate(request.Structure);
        var matches = conditions
            .OrderBy(c => c.Key)
            .Select(c => TraceStructureSqlBuilder.ConditionMatch(c.Value, c.Key, parameters, promoted))
            .ToArray();

        parameters.AddParameter("msTraceIds", request.TraceIds.ToArray());
        parameters.AddParameter("msPerTrace", Math.Clamp(request.SpansPerTrace, 1, MaxSpansPerTrace));
        var where = $"TraceId IN {{msTraceIds:Array(String)}} AND ({string.Join(" OR ", matches)}){ServiceScope.Suffix(parameters)}";

        var sql = "SELECT TraceId, SpanId, ServiceName, Name, StartTime, DurationNano, count() OVER (PARTITION BY TraceId) AS Matched\n" +
            $"FROM spans WHERE {where}\n" +
            "ORDER BY TraceId, StartTime, SpanId\n" +
            "LIMIT {msPerTrace:UInt32} BY TraceId";
        return new MatchedSpansSql(sql, parameters);
    }
}
