using Flare.Api.Query;

namespace Flare.Api.Tempo;

/// <summary>One attribute of a listed span: <see cref="Number"/> for a nested-set position, else <see cref="Text"/>.</summary>
public readonly record struct SelectedAttribute(string Key, string? Text, long? Number = null);

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
/// of a key wins. Event attributes come from the first event that has any of the selected event keys.
/// </para>
/// </remarks>
public static class TempoSpanSelector
{
    /// <summary>Deepest ancestor a <c>&amp;&gt;&gt;</c> looks at, as in <see cref="TraceStructureSqlBuilder.MaxWalkDepth"/>.</summary>
    public const int MaxWalkDepth = TraceStructureSqlBuilder.MaxWalkDepth;

    private const string ServiceNameKey = "service.name";

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
        var unionBits = 0u;
        foreach (var relation in plan.UnionDescendants)
        {
            unionBits |= Bit(relation.Ancestor) | Bit(relation.Descendant);
        }

        // The conditions that are not on either side of a union keep their ordinary meaning: every span matching one is listed.
        var plainBits = 0u;
        foreach (var condition in plan.Conditions)
        {
            if (condition.Name is { Length: 1 } name)
            {
                plainBits |= Bit(char.ToUpperInvariant(name[0]));
            }
        }

        plainBits &= ~unionBits;

        var included = new bool[count];
        for (var i = 0; i < count; i++)
        {
            included[i] = (rows[start + i].MatchMask & plainBits) != 0;
        }

        foreach (var relation in plan.UnionDescendants)
        {
            var ancestorBit = Bit(relation.Ancestor);
            var descendantBit = Bit(relation.Descendant);
            for (var i = 0; i < count; i++)
            {
                if ((rows[start + i].MatchMask & descendantBit) == 0)
                {
                    continue;
                }

                var hasAncestor = false;
                var p = parent[i];
                for (var depth = 0; p >= 0 && depth < MaxWalkDepth; depth++, p = parent[p])
                {
                    if ((rows[start + p].MatchMask & ancestorBit) != 0)
                    {
                        included[p] = true;
                        hasAncestor = true;
                    }
                }

                if (hasAncestor)
                {
                    included[i] = true;
                }
            }
        }

        return included;
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

        void Add(string key, string? text, long? number = null)
        {
            if (seen.Add(key))
            {
                attributes.Add(new SelectedAttribute(key, text, number));
            }
        }

        if (row.ServiceName.Length > 0)
        {
            Add(ServiceNameKey, row.ServiceName);
        }

        var eventAttributes = FirstEvent(row, plan);
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
                    }

                    break;
                case TempoSelectScope.Span:
                    AddValue(field.Key, row.SpanAttributes);
                    break;
                case TempoSelectScope.Resource:
                    AddValue(field.Key, row.ResourceAttributes);
                    break;
                default:
                    AddValue(field.Key, eventAttributes);
                    break;
            }
        }

        // What the query compared on, which the rows carry although no field asked for it.
        foreach (var (key, value) in TraceMatchedSpansQueryBuilder.MergeEchoed(row.SpanAttributes, row.ResourceAttributes))
        {
            Add(key, value);
        }

        return attributes;

        void AddValue(string key, IReadOnlyDictionary<string, string> source)
        {
            if (source.TryGetValue(key, out var value) && value.Length > 0)
            {
                Add(key, value);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> FirstEvent(SpanTreeRow row, TempoSearchPlan plan)
    {
        if (!plan.Select.Any(f => f.Scope == TempoSelectScope.Event))
        {
            return EmptyMap;
        }

        foreach (var attributes in row.EventAttributes)
        {
            if (attributes.Count > 0)
            {
                return attributes;
            }
        }

        return EmptyMap;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyMap = new Dictionary<string, string>();

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
