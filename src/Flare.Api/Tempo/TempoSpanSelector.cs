using Flare.Api.Query;

namespace Flare.Api.Tempo;

/// <summary>
/// One attribute of a listed span: <see cref="Number"/> for a nested-set position, else <see cref="Text"/>. <see cref="Infer"/> is set for
/// the values of the span, resource and event attribute maps, which the writer types when the text is unambiguously a number or a
/// bool (ADR-0230); the intrinsics and the service name are always text.
/// </summary>
public readonly record struct SelectedAttribute(string Key, string? Text, long? Number = null, bool Infer = false);

public sealed record SelectedSpan(string SpanId, string Name, string ServiceName, DateTimeOffset StartTime, ulong DurationNano, IReadOnlyList<SelectedAttribute> Attributes);

/// <summary>The spans a search lists for one trace: the first few of <see cref="Matched"/>.</summary>
public sealed record TraceSelectedSpans(string TraceId, int Matched, IReadOnlyList<SelectedSpan> Spans);

/// <summary>
/// Decides, for the traces of a Tempo search, which spans the result lists and what each carries (ADR-0227). Pure, no
/// ClickHouse dependency: it works on the rows <see cref="TraceSpanSelectQueryBuilder"/> reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which spans.</b> A span is listed when it satisfies a condition that is not part of a <c>&amp;&gt;&gt;</c>, or when it
/// takes part in one: a span matching the right-hand side with a matching span above it, or a span matching the left-hand
/// side with a matching span below it (Tempo's union descendant, which returns both sides). Ancestors are followed up to
/// <see cref="MaxWalkDepth"/> levels, as the structural trace query does.
/// </para>
/// <para>
/// <b>Nested sets.</b> A depth-first numbering of the trace's span tree, as Tempo assigns it: a span's
/// <c>nestedSetLeft</c> is the next number when it is entered and its <c>nestedSetRight</c> the next when it is left, so
/// another span is below it exactly when its numbers lie inside; <c>nestedSetParent</c> is the parent's left number, or -1
/// for a root. Children are visited in start-time order. A span whose parent is not among the rows (never ingested, or past
/// the per-trace cap) is numbered as a root, after the true roots.
/// </para>
/// <para>
/// <b>Attributes.</b> <c>service.name</c> first, then the selected fields in the order written, then whatever else the rows
/// carry (the attributes the query compared on). An attribute is keyed by its name without the scope, as Tempo returns it
/// (<c>resource.service.name</c> is <c>service.name</c>, <c>event.exception.type</c> is <c>exception.type</c>), and the first
/// of a key wins, except for event attributes: those of every event are listed, one event after the other, each event's together (ADR-0230).
/// Attribute-map values are flagged for typing by the writer (<see cref="SelectedAttribute.Infer"/>).
/// </para>
/// </remarks>
public static class TempoSpanSelector
{
    /// <summary>Deepest ancestor a <c>&amp;&gt;&gt;</c> looks at, as in <see cref="TraceStructureSqlBuilder.MaxWalkDepth"/>.</summary>
    public const int MaxWalkDepth = TraceStructureSqlBuilder.MaxWalkDepth;

    private const string ServiceNameKey = "service.name";

    /// <summary>The most events of one span whose attributes a result lists.</summary>
    public const int MaxEventsPerSpan = 50;

    /// <param name="rows">Grouped by trace, each trace's spans in start-time order.</param>
    public static IReadOnlyList<TraceSelectedSpans> Select(IReadOnlyList<SpanTreeRow> rows, TempoSearchPlan plan, int spansPerSpanSet)
    {
        var traces = new List<TraceSelectedSpans>();
        var start = 0;
        while (start < rows.Count)
        {
            var end = start + 1;
            while (end < rows.Count && rows[end].TraceId == rows[start].TraceId)
            {
                end++;
            }

            var spans = SelectTrace(rows, start, end, plan, spansPerSpanSet, out var matched);
            traces.Add(new TraceSelectedSpans(rows[start].TraceId, matched, spans));
            start = end;
        }

        return traces;
    }

    private static List<SelectedSpan> SelectTrace(IReadOnlyList<SpanTreeRow> rows, int start, int end, TempoSearchPlan plan, int spansPerSpanSet, out int matched)
    {
        var count = end - start;
        var parent = ParentIndexes(rows, start, count);

        var included = Included(rows, start, parent, plan);
        var order = Enumerable.Range(0, count).Where(i => included[i]).ToList();
        matched = order.Count;

        int[]? left = null;
        int[]? right = null;
        int[]? parentLeft = null;
        if (plan.Select.Any(f => f.Scope == TempoSelectScope.Intrinsic && f.Key.StartsWith("nestedSet", StringComparison.Ordinal)))
        {
            (left, right, parentLeft) = NestedSets(parent);
            // The tree reads best, and truncates best, in the order of the walk.
            order.Sort((a, b) => left[a].CompareTo(left[b]));
        }

        var spans = new List<SelectedSpan>();
        foreach (var i in order.Take(spansPerSpanSet))
        {
            var row = rows[start + i];
            spans.Add(new SelectedSpan(row.SpanId, row.Name, row.ServiceName, row.StartTime, row.DurationNano, Attributes(row, plan, left?[i], right?[i], parentLeft?[i])));
        }

        return spans;
    }

    /// <summary>For each row of a trace, the index (within the trace) of its parent, or -1 for a root or a span whose parent is not there.</summary>
    internal static int[] ParentIndexes(IReadOnlyList<SpanTreeRow> rows, int start, int count)
    {
        var byId = new Dictionary<string, int>(count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            byId.TryAdd(rows[start + i].SpanId, i);
        }

        var parent = new int[count];
        for (var i = 0; i < count; i++)
        {
            var parentId = rows[start + i].ParentSpanId;
            parent[i] = parentId.Length > 0 && byId.TryGetValue(parentId, out var p) && p != i ? p : -1;
        }

        return parent;
    }

    private static bool[] Included(IReadOnlyList<SpanTreeRow> rows, int start, int[] parent, TempoSearchPlan plan)
    {
        var count = parent.Length;
        var relationBits = 0u;
        foreach (var relation in plan.Relations)
        {
            relationBits |= Bit(relation.Left) | Bit(relation.Right);
        }

        // The conditions that are not on either side of a relation keep their ordinary meaning: every span matching one is listed.
        var plainBits = 0u;
        foreach (var condition in plan.Conditions)
        {
            if (condition.Name is { Length: 1 } name)
            {
                plainBits |= Bit(char.ToUpperInvariant(name[0]));
            }
        }

        plainBits &= ~relationBits;

        var included = new bool[count];
        for (var i = 0; i < count; i++)
        {
            included[i] = (rows[start + i].MatchMask & plainBits) != 0;
        }

        foreach (var relation in plan.Relations)
        {
            var leftBit = Bit(relation.Left);
            var rightBit = Bit(relation.Right);
            var siblings = relation.Kind == TraceRelationKind.Sibling ? SiblingGroups(rows, start, count, leftBit) : null;
            for (var i = 0; i < count; i++)
            {
                if ((rows[start + i].MatchMask & rightBit) == 0)
                {
                    continue;
                }

                // The left-hand spans related to this right-hand one.
                var related = Related(rows, start, parent, i, relation.Kind, leftBit, siblings);
                if (related.Count > 0 != relation.Negated)
                {
                    included[i] = true;
                }

                // A union returns the left-hand side too: every left-hand span that has this one as its relation.
                if (relation.Union && related.Count > 0)
                {
                    foreach (var j in related)
                    {
                        included[j] = true;
                    }
                }
            }
        }

        return included;
    }

    /// <summary>The spans (indexes within the trace) matching <paramref name="leftBit"/> that stand in <paramref name="kind"/> to span <paramref name="i"/>: its parent, its ancestors, or its siblings.</summary>
    private static List<int> Related(IReadOnlyList<SpanTreeRow> rows, int start, int[] parent, int i, TraceRelationKind kind, uint leftBit, Dictionary<string, List<int>>? siblings)
    {
        var related = new List<int>();
        switch (kind)
        {
            case TraceRelationKind.Child:
                if (parent[i] >= 0 && (rows[start + parent[i]].MatchMask & leftBit) != 0)
                {
                    related.Add(parent[i]);
                }

                break;
            case TraceRelationKind.Descendant:
                var p = parent[i];
                for (var depth = 0; p >= 0 && depth < MaxWalkDepth; depth++, p = parent[p])
                {
                    if ((rows[start + p].MatchMask & leftBit) != 0)
                    {
                        related.Add(p);
                    }
                }

                break;
            default:
                if (siblings!.TryGetValue(rows[start + i].ParentSpanId, out var group))
                {
                    related.AddRange(group.Where(j => j != i));
                }

                break;
        }

        return related;
    }

    /// <summary>The spans matching <paramref name="leftBit"/>, grouped by their parent id (the empty id groups the roots, which are siblings of each other, as in Tempo).</summary>
    private static Dictionary<string, List<int>> SiblingGroups(IReadOnlyList<SpanTreeRow> rows, int start, int count, uint leftBit)
    {
        var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            if ((rows[start + i].MatchMask & leftBit) != 0)
            {
                if (!groups.TryGetValue(rows[start + i].ParentSpanId, out var list))
                {
                    groups[rows[start + i].ParentSpanId] = list = [];
                }

                list.Add(i);
            }
        }

        return groups;
    }

    private static uint Bit(char letter) => 1u << (char.ToUpperInvariant(letter) - 'A');

    /// <summary>The depth-first numbering of a forest given each node's parent (-1 for a root); children in index order. Iterative, so a deep trace cannot overflow the stack.</summary>
    internal static (int[] Left, int[] Right, int[] ParentLeft) NestedSets(int[] parent)
    {
        var count = parent.Length;
        var children = new List<int>?[count];
        for (var i = 0; i < count; i++)
        {
            if (parent[i] >= 0)
            {
                (children[parent[i]] ??= []).Add(i);
            }
        }

        var left = new int[count];
        var right = new int[count];
        var parentLeft = new int[count];
        var visited = new bool[count];
        var counter = 1;
        var stack = new Stack<(int Node, int Next)>();

        void Walk(int root)
        {
            visited[root] = true;
            left[root] = counter++;
            parentLeft[root] = -1;
            stack.Push((root, 0));
            while (stack.Count > 0)
            {
                var (node, next) = stack.Pop();
                if (children[node] is { } kids && next < kids.Count)
                {
                    stack.Push((node, next + 1));
                    var child = kids[next];
                    if (visited[child])
                    {
                        continue;
                    }

                    visited[child] = true;
                    left[child] = counter++;
                    parentLeft[child] = left[node];
                    stack.Push((child, 0));
                }
                else
                {
                    right[node] = counter++;
                }
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (parent[i] < 0)
            {
                Walk(i);
            }
        }

        // Only a parent cycle leaves a span unreached; number it rather than leave it without a position.
        for (var i = 0; i < count; i++)
        {
            if (!visited[i])
            {
                Walk(i);
            }
        }

        return (left, right, parentLeft);
    }

    private static List<SelectedAttribute> Attributes(SpanTreeRow row, TempoSearchPlan plan, int? left, int? right, int? parentLeft)
    {
        var attributes = new List<SelectedAttribute>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string key, string? text, long? number = null, bool infer = false)
        {
            if (seen.Add(key))
            {
                attributes.Add(new SelectedAttribute(key, text, number, infer));
            }
        }

        if (row.ServiceName.Length > 0)
        {
            Add(ServiceNameKey, row.ServiceName);
        }

        var eventsWritten = false;
        foreach (var field in plan.Select)
        {
            switch (field.Scope)
            {
                case TempoSelectScope.Intrinsic:
                    switch (field.Key)
                    {
                        case "name":
                            Add("name", row.Name);
                            break;
                        case "status":
                            Add("status", StatusKeyword(row.StatusCode));
                            break;
                        case "statusMessage" when row.StatusMessage.Length > 0:
                            Add("statusMessage", row.StatusMessage);
                            break;
                        case "kind":
                            Add("kind", KindKeyword(row.Kind));
                            break;
                        case "nestedSetLeft" when left is { } l:
                            Add("nestedSetLeft", null, l);
                            break;
                        case "nestedSetRight" when right is { } r:
                            Add("nestedSetRight", null, r);
                            break;
                        case "nestedSetParent" when parentLeft is { } p:
                            Add("nestedSetParent", null, p);
                            break;

                        // rootName, rootServiceName and traceDuration are fields of the trace, which every result row already carries
                        // (rootTraceName, rootServiceName, durationMs), as Tempo's own response does; duration is the span's durationNanos.
                    }

                    break;
                case TempoSelectScope.Span:
                    AddValue(field.Key, row.SpanAttributes);
                    break;
                case TempoSelectScope.Resource:
                    AddValue(field.Key, row.ResourceAttributes);
                    break;
                default:
                    if (!eventsWritten)
                    {
                        eventsWritten = true;
                        AddEvents(row, plan, attributes);
                    }

                    break;
            }
        }

        // What the query compared on, which the rows carry although no field asked for it.
        foreach (var (key, value) in TraceMatchedSpansQueryBuilder.MergeEchoed(row.SpanAttributes, row.ResourceAttributes))
        {
            Add(key, value, infer: true);
        }

        return attributes;

        void AddValue(string key, IReadOnlyDictionary<string, string> source)
        {
            if (source.TryGetValue(key, out var value) && value.Length > 0)
            {
                Add(key, value, infer: true);
            }
        }
    }

    /// <summary>
    /// The selected event attributes of every event of the span (at most <see cref="MaxEventsPerSpan"/>), one event after the other
    /// in the span's event order, each event's attributes together in the order the fields were written: the message, type and
    /// stack trace of one exception stay side by side. A key therefore appears once per event that has it (ADR-0230); a reader that
    /// takes the first of a key sees the first event, one that keeps the last sees the last.
    /// </summary>
    private static void AddEvents(SpanTreeRow row, TempoSearchPlan plan, List<SelectedAttribute> attributes)
    {
        var keys = plan.Select.Where(f => f.Scope == TempoSelectScope.Event).Select(f => f.Key).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var eventAttributes in row.EventAttributes.Take(MaxEventsPerSpan))
        {
            foreach (var key in keys)
            {
                if (eventAttributes.TryGetValue(key, out var value) && value.Length > 0)
                {
                    attributes.Add(new SelectedAttribute(key, value, Infer: true));
                }
            }
        }
    }

    internal static string StatusKeyword(string statusCode) => statusCode switch
    {
        "STATUS_CODE_ERROR" => "error",
        "STATUS_CODE_OK" => "ok",
        _ => "unset",
    };

    internal static string KindKeyword(byte kind) => kind switch
    {
        1 => "internal",
        2 => "server",
        3 => "client",
        4 => "producer",
        5 => "consumer",
        _ => "unspecified",
    };
}
