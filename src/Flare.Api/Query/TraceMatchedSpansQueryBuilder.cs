using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>The spans of <see cref="TraceIds"/> that match a <see cref="TraceStructureFilter"/>'s conditions - the Tempo API's <c>spanSets</c> (ADR-0219).</summary>
public sealed record MatchedSpansRequest(IReadOnlyList<string> TraceIds, TraceStructureFilter Structure, int SpansPerTrace);

/// <summary>
/// One matching span of a trace, as much of it as a Tempo span set shows. <see cref="Attributes"/> are the span's and
/// resource's attributes the query compared on (see <see cref="TraceMatchedSpansQueryBuilder.EchoedKeys"/>), the ones it
/// has, span bag first.
/// </summary>
public sealed record MatchedSpan(string SpanId, string ServiceName, string Name, DateTimeOffset StartTime, ulong DurationNano, IReadOnlyList<KeyValuePair<string, string>> Attributes);

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
/// <para>
/// Each span also carries the attributes the conditions compared on, so a result row shows why it matched
/// (<c>http.status_code</c> next to a <c>&gt;= 500</c> query). Only those keys are read out of the attribute
/// maps, at most <see cref="MaxEchoedAttributes"/>, never the whole bag.
/// </para>
/// </remarks>
public static class TraceMatchedSpansQueryBuilder
{
    public const int MaxSpansPerTrace = 100;

    /// <summary>The most attribute keys echoed on a span, across both bags, so a query naming many attributes cannot widen every row.</summary>
    public const int MaxEchoedAttributes = 20;

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

        var (spanKeys, resourceKeys) = EchoedKeys(conditions.Values);
        parameters.AddParameter("msTraceIds", request.TraceIds.ToArray());
        parameters.AddParameter("msPerTrace", Math.Clamp(request.SpansPerTrace, 1, MaxSpansPerTrace));
        var where = $"TraceId IN {{msTraceIds:Array(String)}} AND ({string.Join(" OR ", matches)}){ServiceScope.Suffix(parameters)}";

        var sql = "SELECT TraceId, SpanId, ServiceName, Name, StartTime, DurationNano, count() OVER (PARTITION BY TraceId) AS Matched, " +
            $"{EchoedMap("SpanAttributes", "msSpanKeys", spanKeys, parameters)} AS EchoedSpanAttributes, " +
            $"{EchoedMap("ResourceAttributes", "msResourceKeys", resourceKeys, parameters)} AS EchoedResourceAttributes\n" +
            $"FROM spans WHERE {where}\n" +
            "ORDER BY TraceId, StartTime, SpanId\n" +
            "LIMIT {msPerTrace:UInt32} BY TraceId";
        return new MatchedSpansSql(sql, parameters);
    }

    /// <summary>
    /// The attribute keys the conditions compare on, per bag, in condition order without repeats and at most
    /// <see cref="MaxEchoedAttributes"/> in all. A span attribute with old and new semantic-convention spellings
    /// (<c>http.method</c>, <c>http.request.method</c>) lists all of them, since the condition matches any; a span
    /// carries whichever it has. Scope attributes are not echoed: TraceQL has no scope for them.
    /// </summary>
    internal static (string[] Span, string[] Resource) EchoedKeys(IEnumerable<TraceSpanCondition> conditions)
    {
        var span = new List<string>();
        var resource = new List<string>();

        void Add(List<string> keys, string key)
        {
            if (span.Count + resource.Count < MaxEchoedAttributes && !keys.Contains(key, StringComparer.Ordinal))
            {
                keys.Add(key);
            }
        }

        foreach (var attribute in conditions.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).SelectMany(c => c.Attributes ?? []))
        {
            switch (attribute.Bag)
            {
                case SpanAttributeBag.Span:
                    foreach (var key in SemconvAliases.Group(attribute.Key) ?? [attribute.Key])
                    {
                        Add(span, key);
                    }

                    break;
                case SpanAttributeBag.Resource:
                    Add(resource, attribute.Key);
                    break;
            }
        }

        return (span.ToArray(), resource.ToArray());
    }

    /// <summary>
    /// The span's and the resource's echoed attributes as one list: the span's first, then the resource's that the span
    /// does not also have, each by key. Empty-valued entries are dropped (a key a map reports as empty is not set).
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> MergeEchoed(IReadOnlyDictionary<string, string> span, IReadOnlyDictionary<string, string> resource) =>
    [
        .. span.Where(a => a.Value.Length > 0).OrderBy(a => a.Key, StringComparer.Ordinal),
        .. resource.Where(a => a.Value.Length > 0 && !span.ContainsKey(a.Key)).OrderBy(a => a.Key, StringComparer.Ordinal),
    ];

    /// <summary>A map holding only <paramref name="keys"/> of <paramref name="column"/>; a typed empty map, without reading the column, when there are none.</summary>
    private static string EchoedMap(string column, string parameter, string[] keys, ClickHouseParameterCollection parameters)
    {
        if (keys.Length == 0)
        {
            return "CAST(map(), 'Map(String, String)')";
        }

        parameters.AddParameter(parameter, keys);
        return $"mapFilter((k, v) -> k IN {{{parameter}:Array(String)}}, {column})";
    }
}
