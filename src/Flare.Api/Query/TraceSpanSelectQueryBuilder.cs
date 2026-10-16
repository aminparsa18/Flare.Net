using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// What a Tempo <c>select()</c> or <c>&amp;&gt;&gt;</c> needs of the traces a search returned: the spans of
/// <see cref="TraceIds"/> (all of them when <see cref="WholeTrace"/>, otherwise those matching a condition), each
/// with which conditions it matches and the attribute values asked for.
/// </summary>
/// <param name="StatusMessage">Whether to read each span's status message (<c>select(statusMessage)</c>); an empty string otherwise.</param>
/// <param name="SpanKeys">Span attribute keys to read; likewise <paramref name="ResourceKeys"/> and, from the span's events, <paramref name="EventKeys"/>.</param>
public sealed record SelectedSpansRequest(
    IReadOnlyList<string> TraceIds,
    TraceStructureFilter Structure,
    IReadOnlyList<string> SpanKeys,
    IReadOnlyList<string> ResourceKeys,
    IReadOnlyList<string> EventKeys,
    bool WholeTrace,
    bool StatusMessage = false);

/// <param name="MatchMask">Bit <c>letter - 'A'</c> is set when the span satisfies that condition of the structure.</param>
/// <param name="StatusCode">The column's label, <c>STATUS_CODE_ERROR</c> and so on.</param>
/// <param name="EventAttributes">For each event of the span, in order, its attributes among the requested keys.</param>
public sealed record SpanTreeRow(
    string TraceId,
    string SpanId,
    string ParentSpanId,
    string Name,
    string ServiceName,
    DateTimeOffset StartTime,
    ulong DurationNano,
    byte Kind,
    string StatusCode,
    uint MatchMask,
    IReadOnlyDictionary<string, string> SpanAttributes,
    IReadOnlyDictionary<string, string> ResourceAttributes,
    IReadOnlyList<IReadOnlyDictionary<string, string>> EventAttributes,
    string StatusMessage = "");

/// <summary>Fully-built <c>SELECT</c> for <see cref="ISpanQueryService.GetSelectedSpansAsync"/>.</summary>
public sealed record SelectedSpansSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="SelectedSpansRequest"/> → parameterized SQL (ADR-0227). Where <see cref="TraceMatchedSpansQueryBuilder"/>
/// lists the first few matching spans, this returns the rows a caller needs to decide for itself which spans to list: a
/// <c>&amp;&gt;&gt;</c> keeps only the spans that take part in the relation, and a nested-set number is a position in the whole
/// trace's span tree, so neither can be cut down in SQL. Each row carries a bit per structure condition
/// (<see cref="TraceStructureSqlBuilder.ConditionMatch"/>, so a search, a metric and this agree on what matches).
/// </summary>
/// <remarks>
/// No time window, as for the matched-spans query: the lookup is by <c>TraceId</c>, the first column of the sort key. At most
/// <see cref="MaxSpansPerTrace"/> spans per trace, the earliest by start time; a longer trace has its later spans numbered as
/// if absent (the spans that lose their parent this way become roots).
/// </remarks>
public static class TraceSpanSelectQueryBuilder
{
    /// <summary>The most spans read per trace.</summary>
    public const int MaxSpansPerTrace = 2000;

    /// <summary>The most traces one request names (a Tempo search returns at most 200), so the rows read stay below <see cref="MaxRows"/>.</summary>
    public const int MaxTraces = 200;

    public const int MaxRows = MaxSpansPerTrace * MaxTraces;

    public static SelectedSpansSql Build(SelectedSpansRequest request, PromotedAttributeColumns? promoted = null)
    {
        if (request.TraceIds.Count == 0)
        {
            throw new ArgumentException("At least one trace id is needed.", nameof(request));
        }

        if (request.TraceIds.Count > MaxTraces)
        {
            throw new ArgumentException($"At most {MaxTraces} traces can be read at once.", nameof(request));
        }

        var parameters = new ClickHouseParameterCollection();
        var (_, conditions) = TraceStructureSqlBuilder.Validate(request.Structure);
        var ordered = conditions.OrderBy(c => c.Key).ToArray();
        var matches = ordered.Select(c => TraceStructureSqlBuilder.ConditionMatch(c.Value, c.Key, parameters, promoted)).ToArray();
        var mask = string.Join(" + ", ordered.Select((c, i) => $"toUInt32({matches[i]}) * {1u << (c.Key - 'A')}"));

        parameters.AddParameter("ssTraceIds", request.TraceIds.ToArray());
        parameters.AddParameter("ssPerTrace", MaxSpansPerTrace);
        var where = $"TraceId IN {{ssTraceIds:Array(String)}}{ServiceScope.Suffix(parameters)}";
        if (!request.WholeTrace)
        {
            where += $" AND ({string.Join(" OR ", matches)})";
        }

        var sql = "SELECT TraceId, SpanId, ParentSpanId, Name, ServiceName, StartTime, DurationNano, Kind, StatusCode, " +
            $"toUInt32({mask}) AS MatchMask, " +
            $"{KeyMap("SpanAttributes", "ssSpanKeys", request.SpanKeys, parameters)} AS SelectedSpanAttributes, " +
            $"{KeyMap("ResourceAttributes", "ssResourceKeys", request.ResourceKeys, parameters)} AS SelectedResourceAttributes, " +
            $"{EventMaps(request.EventKeys, parameters)} AS SelectedEventAttributes, " +
            $"{(request.StatusMessage ? "StatusMessage" : "''")} AS SelectedStatusMessage\n" +
            $"FROM spans WHERE {where}\n" +
            "ORDER BY TraceId, StartTime, SpanId\n" +
            "LIMIT {ssPerTrace:UInt32} BY TraceId";
        return new SelectedSpansSql(sql, parameters);
    }

    /// <summary>A map holding only <paramref name="keys"/> of <paramref name="column"/>; a typed empty map, without reading the column, when there are none.</summary>
    private static string KeyMap(string column, string parameter, IReadOnlyList<string> keys, ClickHouseParameterCollection parameters)
    {
        if (keys.Count == 0)
        {
            return "CAST(map(), 'Map(String, String)')";
        }

        parameters.AddParameter(parameter, keys.ToArray());
        return $"mapFilter((k, v) -> k IN {{{parameter}:Array(String)}}, {column})";
    }

    /// <summary>The attributes among <paramref name="keys"/> of each event of the span; an empty array, without reading the column, when there are none.</summary>
    private static string EventMaps(IReadOnlyList<string> keys, ClickHouseParameterCollection parameters)
    {
        if (keys.Count == 0)
        {
            return "CAST([], 'Array(Map(String, String))')";
        }

        parameters.AddParameter("ssEventKeys", keys.ToArray());
        return "arrayMap(m -> mapFilter((k, v) -> k IN {ssEventKeys:Array(String)}, m), `Events.Attributes`)";
    }
}
