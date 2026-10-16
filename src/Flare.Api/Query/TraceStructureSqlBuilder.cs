using System.Text;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Pure <see cref="TraceStructureFilter"/> → <c>SELECT TraceId</c> subquery translation,
/// used by <see cref="SpanFilterSqlBuilder"/> for <see cref="SpanFilter.Structure"/>. See
/// docs-internal/adr/0069-structural-trace-queries.md.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shape.</b> Spans in the window that match any referenced condition are grouped by
/// trace into, per condition <c>X</c>: <c>hX</c> (has a match), <c>sX</c> (the matches'
/// hashed span ids) and <c>pX</c> (their hashed parent ids, 0 for a root). A bare letter is
/// <c>hX</c>; <c>A -> B</c> is <c>hasAny(sA, pB)</c> - one pass, no join.
/// </para>
/// <para>
/// <c>A => B</c> needs the spans in between, which match neither condition, so it runs in
/// two stages. Stage 1 is the pass above with each <c>=></c> swapped for a bound that can
/// only over-match: <c>hA AND hB</c> where the relation counts for the trace, and the
/// direct-child test where it's negated. Stage 2 re-reads every span of just those
/// candidate traces (a <c>TraceId IN</c> lookup, cheap on the <c>TraceId</c>-first sort
/// key), collects the whole trace's <c>allS</c>/<c>allP</c> arrays and evaluates exactly:
/// the direct-child test first, then an <c>arrayFold</c> that walks every <c>B</c> match's
/// ancestors one level per step, looking for an <c>A</c> match. The walk runs at most
/// <c>least(</c><see cref="MaxWalkDepth"/><c>, span count)</c> steps, so short traces
/// stay cheap and a malformed parent cycle can't loop.
/// </para>
/// <para>
/// Only spans that start inside the window take part - a trace straddling its edge, or
/// one with an ancestor that was never ingested, can miss a <c>=></c> match. The span ids
/// are hashed (<c>cityHash64</c>) only to keep the per-trace arrays small.
/// </para>
/// </remarks>
public static class TraceStructureSqlBuilder
{
    public const int MaxConditions = 6;

    /// <summary>Deepest ancestor a <c>=></c> walk looks at - well past any real call chain.</summary>
    public const int MaxWalkDepth = 64;

    private static readonly string[] StatusCodes = ["STATUS_CODE_UNSET", "STATUS_CODE_OK", "STATUS_CODE_ERROR"];

    /// <summary>The highest OTel <c>SpanKind</c> number (consumer).</summary>
    private const byte MaxSpanKind = 5;

    internal const string SpanHash = "cityHash64(SpanId)";
    internal const string ParentHash = "if(ParentSpanId = '', toUInt64(0), cityHash64(ParentSpanId))";

    /// <summary>
    /// The <c>SELECT TraceId ...</c> subquery (no surrounding parentheses). Reads the window
    /// from the <c>from</c>/<c>to</c> parameters the caller already bound. Throws
    /// <see cref="ArgumentException"/> with a user-facing message when the filter is invalid.
    /// </summary>
    public static string BuildTraceIdQuery(TraceStructureFilter structure, ClickHouseParameterCollection parameters, PromotedAttributeColumns? promoted)
    {
        var (expression, conditions) = Validate(structure);

        var matches = new SortedDictionary<char, string>();
        foreach (var (letter, condition) in conditions)
        {
            matches[letter] = ConditionMatch(condition, letter, parameters, promoted);
        }

        var relations = new List<TraceStructureRelation>();
        CollectRelations(expression, relations);
        // A walk over the whole trace is needed for a descendant or ancestor test, in either direction.
        var hasDescendant = relations.Any(r => r.Kind is TraceRelationKind.Descendant or TraceRelationKind.Ancestor);

        var aliases = new List<string>();
        foreach (var (letter, match) in matches)
        {
            aliases.Add($"countIf({match}) > 0 AS h{letter}");

            // sX: the span hashes of the side that is the parent or ancestor; pX: the parent hashes of the side that is the child or descendant.
            if (relations.Any(r => r.Kind != TraceRelationKind.Sibling && r.Upper == letter))
            {
                aliases.Add($"groupArrayIf({SpanHash}, {match}) AS s{letter}");
            }

            if (relations.Any(r => r.Kind != TraceRelationKind.Sibling && r.Lower == letter)
                || relations.Any(r => r.Kind == TraceRelationKind.Sibling && r.Parent == letter))
            {
                aliases.Add($"groupArrayIf({ParentHash}, {match}) AS p{letter}");
            }
        }

        // A sibling test is made per right-hand span, which also has to know whether it is itself a left-hand span: it is not its own sibling.
        foreach (var (left, right) in relations.Where(r => r.Kind == TraceRelationKind.Sibling).Select(r => (r.Parent, r.Child)).Distinct())
        {
            aliases.Add($"groupArrayIf(({ParentHash}, toUInt8({matches[left]})), {matches[right]}) AS b{left}{right}");
        }

        var window = "StartTime >= {from:DateTime64(9)} AND StartTime < {to:DateTime64(9)}";
        var prefilter = new StringBuilder(window);
        // Every condition names its services (one, or a list), so no span of another service can match any of them.
        if (conditions.Values.All(c => !string.IsNullOrEmpty(c.ServiceName) || c.ServiceNames is { Count: > 0 }))
        {
            // A condition with both is their intersection; its list alone is a superset, which is all a prefilter needs.
            var services = conditions.Values.SelectMany(c => c.ServiceNames is { Count: > 0 } ? c.ServiceNames : [c.ServiceName!]);
            parameters.AddParameter("tsServices", services.Distinct(StringComparer.Ordinal).ToArray());
            prefilter.Append(" AND ServiceName IN {tsServices:Array(String)}");
        }

        prefilter.Append(ServiceScope.Suffix(parameters));
        prefilter.Append($" AND ({string.Join(" OR ", matches.Values)})");

        if (!hasDescendant)
        {
            return Stage(aliases, prefilter.ToString(), ExpressionSql(expression, exact: true, positive: true));
        }

        var candidates = Stage(aliases, prefilter.ToString(), ExpressionSql(expression, exact: false, positive: true));
        var fullTrace = new List<string>(aliases)
        {
            $"groupArray({SpanHash}) AS allS",
            $"groupArray({ParentHash}) AS allP",
        };
        return Stage(fullTrace, $"{window} AND TraceId GLOBAL IN ({candidates})", ExpressionSql(expression, exact: true, positive: true));
    }

    /// <summary>
    /// Parses and checks <paramref name="structure"/>: 1 to <see cref="MaxConditions"/>
    /// uniquely-lettered conditions, an expression that only mentions defined letters, each
    /// of those with something to match on, and one that can't hold for a trace with no matching
    /// span at all (<c>NOT A</c> alone would mean "every other trace in the table").
    /// </summary>
    public static (TraceStructureNode Expression, IReadOnlyDictionary<char, TraceSpanCondition> Conditions) Validate(TraceStructureFilter structure)
    {
        if (structure.Conditions is not { Count: > 0 } conditions || conditions.Count > MaxConditions)
        {
            throw new ArgumentException($"A trace structure needs 1 to {MaxConditions} span conditions.");
        }

        var byLetter = new Dictionary<char, TraceSpanCondition>();
        foreach (var condition in conditions)
        {
            var name = condition.Name?.Trim() ?? string.Empty;
            if (name.Length != 1 || !char.IsAsciiLetter(name[0]))
            {
                throw new ArgumentException($"Condition name '{condition.Name}' must be a single letter (A-Z).");
            }

            var letter = char.ToUpperInvariant(name[0]);
            if (!byLetter.TryAdd(letter, condition))
            {
                throw new ArgumentException($"Condition {letter} is defined twice.");
            }

            if (!string.IsNullOrEmpty(condition.StatusCode) && !StatusCodes.Contains(condition.StatusCode))
            {
                throw new ArgumentException($"Condition {letter} has an unknown status '{condition.StatusCode}'.");
            }

            if (condition.Kind > MaxSpanKind || condition.Kinds?.Any(k => k > MaxSpanKind) == true)
            {
                throw new ArgumentException($"Condition {letter} has an unknown span kind {condition.Kinds?.Max() ?? condition.Kind}; use 0 (unspecified) to {MaxSpanKind} (consumer).");
            }

            if (!Enum.IsDefined(condition.SpanNameMatch))
            {
                throw new ArgumentException($"Condition {letter} has an unknown span name match '{condition.SpanNameMatch}'.");
            }
        }

        var expression = TraceStructureExpression.Parse(structure.Expression);
        var used = new Dictionary<char, TraceSpanCondition>();
        foreach (var letter in TraceStructureExpression.Letters(expression))
        {
            if (!byLetter.TryGetValue(letter, out var condition))
            {
                throw new ArgumentException($"The expression uses condition {letter}, which isn't defined.");
            }

            // Only a referenced condition must match on something - an unused blank one (a
            // card the user added but hasn't filled in yet) is just ignored.
            if (string.IsNullOrEmpty(condition.ServiceName) && condition.ServiceNames is not { Count: > 0 } && string.IsNullOrEmpty(condition.SpanName) && string.IsNullOrEmpty(condition.StatusCode)
                && condition.Kind is null && condition.Kinds is not { Count: > 0 } && condition.RootSpan is null && condition.MinDurationNano is null && condition.MaxDurationNano is null && condition.Attributes is not { Count: > 0 })
            {
                throw new ArgumentException($"Condition {letter} needs a service, span name, status, duration or attribute filter.");
            }

            used[letter] = condition;
        }

        if (MatchesEmptyTrace(expression))
        {
            throw new ArgumentException("The expression also matches traces with none of its spans (e.g. NOT A on its own) - combine it with a condition the trace must have.");
        }

        return (expression, used);
    }

    private static string Stage(IEnumerable<string> aliases, string where, string condition) =>
        $"SELECT TraceId FROM (SELECT TraceId, {string.Join(", ", aliases)} FROM spans WHERE {where} GROUP BY TraceId) WHERE {condition}";

    /// <summary>
    /// <paramref name="node"/> over the per-trace aliases. <paramref name="exact"/> = false
    /// is stage 1's over-matching bound (no <c>allS</c>/<c>allP</c> yet): a <c>=></c> reads
    /// as <c>hA AND hB</c> when <paramref name="positive"/> (an even number of enclosing
    /// <c>NOT</c>s), or as the direct-child test (a sub-case of it) when negated - either
    /// way the whole expression can only err towards keeping a trace.
    /// </summary>
    private static string ExpressionSql(TraceStructureNode node, bool exact, bool positive) => node switch
    {
        TraceStructureHas has => $"h{has.Condition}",
        TraceStructureRelation r => RelationSql(r, exact, positive),
        TraceStructureNot not => $"NOT {ExpressionSql(not.Operand, exact, !positive)}",
        TraceStructureAnd and => $"({ExpressionSql(and.Left, exact, positive)} AND {ExpressionSql(and.Right, exact, positive)})",
        TraceStructureOr or => $"({ExpressionSql(or.Left, exact, positive)} OR {ExpressionSql(or.Right, exact, positive)})",
        _ => throw new InvalidOperationException($"Unknown node {node.GetType().Name}."),
    };

    /// <summary>
    /// One relation over the per-trace aliases: whether some right-hand span (<c>Child</c>) has a left-hand one (<c>Parent</c>) related to it as
    /// <see cref="TraceStructureRelation.Kind"/> says, or - negated - has none. For <see cref="TraceRelationKind.Parent"/> and
    /// <see cref="TraceRelationKind.Ancestor"/> (ADR-0235) the question is turned round: some right-hand span is the parent or ancestor of a left-hand
    /// one, or - negated - of none; the aliases are then <c>s</c> of the right-hand letter (the upper spans) and <c>p</c> of the left-hand one.
    /// Children, parents, siblings and their negations are exact in stage 1.
    /// A descendant or ancestor test needs the spans in between, so stage 1 (<paramref name="exact"/> false) uses a bound that can only err towards
    /// keeping a trace: <c>hA AND hB</c> for <c>=></c> (the direct test, a sub-case, when negated); for <c>!=></c> <c>hB</c>
    /// (some right-hand span exists) in a positive position and false in a negated one.
    /// </summary>
    private static string RelationSql(TraceStructureRelation r, bool exact, bool positive)
    {
        var upper = r.Upper;
        var lower = r.Lower;
        switch (r.Kind)
        {
            case TraceRelationKind.Child:
                return r.Negated ? $"arrayExists(p -> NOT has(s{r.Parent}, p), p{r.Child})" : DirectChildSql(r);
            case TraceRelationKind.Parent:
                // Some right-hand span that is no left-hand span's parent: its hash is not among the left-hand spans' parent hashes.
                return r.Negated ? $"arrayExists(u -> NOT has(p{lower}, u), s{upper})" : DirectChildSql(r);
            case TraceRelationKind.Sibling:
            {
                var sibling = $"arrayExists(b -> if(b.2 = 1, countEqual(p{r.Parent}, b.1) >= 2, has(p{r.Parent}, b.1)), b{r.Parent}{r.Child})";
                return r.Negated ? $"arrayExists(b -> NOT if(b.2 = 1, countEqual(p{r.Parent}, b.1) >= 2, has(p{r.Parent}, b.1)), b{r.Parent}{r.Child})" : sibling;
            }
        }

        var ancestor = r.Kind == TraceRelationKind.Ancestor;
        if (!r.Negated)
        {
            return exact
                ? $"({DirectChildSql(r)} OR arrayFold((acc, x) -> (arrayMap(h -> if(h = 0, toUInt64(0), allP[indexOf(allS, h)]), acc.1), acc.2 OR hasAny(acc.1, s{upper})), " +
                  $"range(least({MaxWalkDepth}, length(allS))), (p{lower}, false)).2)"
                : positive ? $"(h{r.Parent} AND h{r.Child})" : DirectChildSql(r);
        }

        if (!exact)
        {
            return positive ? $"h{r.Child}" : "0";
        }

        // Not an ancestor: some right-hand span that is above no left-hand span. The fold collects every ancestor of every left-hand span.
        if (ancestor)
        {
            return "arrayExists(u -> NOT has(arrayFold((acc, x) -> (arrayMap(h -> if(h = 0, toUInt64(0), allP[indexOf(allS, h)]), acc.1), arrayConcat(acc.2, acc.1)), " +
                $"range(least({MaxWalkDepth}, length(allS))), (p{lower}, CAST([], 'Array(UInt64)'))).2, u), s{upper})";
        }

        // Not a descendant: some right-hand span whose chain of ancestors holds no left-hand span. The fold keeps, per right-hand span, whether it has met one.
        return "arrayExists(f -> f = 0, arrayFold((acc, x) -> (arrayMap(h -> if(h = 0, toUInt64(0), allP[indexOf(allS, h)]), acc.1), " +
            $"arrayMap((h, f) -> f OR has(s{upper}, h), acc.1, acc.2)), range(least({MaxWalkDepth}, length(allS))), (p{lower}, arrayMap(x -> toUInt8(0), p{lower}))).2)";
    }

    private static string DirectChildSql(TraceStructureRelation r) => $"hasAny(s{r.Upper}, p{r.Lower})";

    /// <summary>The expression's value for a trace with no span matching any condition - every leaf is false there.</summary>
    private static bool MatchesEmptyTrace(TraceStructureNode node) => node switch
    {
        TraceStructureNot not => !MatchesEmptyTrace(not.Operand),
        TraceStructureAnd and => MatchesEmptyTrace(and.Left) && MatchesEmptyTrace(and.Right),
        TraceStructureOr or => MatchesEmptyTrace(or.Left) || MatchesEmptyTrace(or.Right),
        _ => false,
    };

    private static void CollectRelations(TraceStructureNode node, List<TraceStructureRelation> relations)
    {
        switch (node)
        {
            case TraceStructureRelation relation:
                relations.Add(relation);
                break;
            case TraceStructureNot not:
                CollectRelations(not.Operand, relations);
                break;
            case TraceStructureAnd and:
                CollectRelations(and.Left, relations);
                CollectRelations(and.Right, relations);
                break;
            case TraceStructureOr or:
                CollectRelations(or.Left, relations);
                CollectRelations(or.Right, relations);
                break;
        }
    }

    /// <summary>One condition's span predicate, parameters bound under a <c>ts{letter}</c> prefix so conditions never collide with each other or the outer filter.</summary>
    internal static string ConditionMatch(TraceSpanCondition condition, char letter, ClickHouseParameterCollection parameters, PromotedAttributeColumns? promoted)
    {
        var clauses = new List<string>();
        if (!string.IsNullOrEmpty(condition.ServiceName))
        {
            parameters.AddParameter($"ts{letter}Service", condition.ServiceName);
            clauses.Add($"ServiceName = {{ts{letter}Service:String}}");
        }

        if (condition.ServiceNames is { Count: > 0 } serviceNames)
        {
            parameters.AddParameter($"ts{letter}Services", serviceNames.ToArray());
            clauses.Add($"ServiceName IN {{ts{letter}Services:Array(String)}}");
        }

        if (!string.IsNullOrEmpty(condition.SpanName))
        {
            parameters.AddParameter($"ts{letter}Name", condition.SpanName);
            clauses.Add(condition.SpanNameMatch switch
            {
                SpanNameMatch.NotEquals => $"Name != {{ts{letter}Name:String}}",
                SpanNameMatch.Regex => $"match(Name, {{ts{letter}Name:String}})",
                SpanNameMatch.NotRegex => $"NOT match(Name, {{ts{letter}Name:String}})",
                _ => $"Name = {{ts{letter}Name:String}}",
            });
        }

        if (!string.IsNullOrEmpty(condition.StatusCode))
        {
            parameters.AddParameter($"ts{letter}Status", condition.StatusCode);
            clauses.Add($"StatusCode {(condition.StatusCodeNegated ? "!=" : "=")} {{ts{letter}Status:String}}");
        }

        if (condition.Kind is { } kind)
        {
            parameters.AddParameter($"ts{letter}Kind", kind);
            clauses.Add($"Kind {(condition.KindNegated ? "!=" : "=")} {{ts{letter}Kind:UInt8}}");
        }

        if (condition.Kinds is { Count: > 0 } kinds)
        {
            parameters.AddParameter($"ts{letter}Kinds", kinds.ToArray());
            clauses.Add($"Kind IN {{ts{letter}Kinds:Array(UInt8)}}");
        }

        if (condition.RootSpan is { } root)
        {
            clauses.Add(root ? "ParentSpanId = ''" : "ParentSpanId != ''");
        }

        if (condition.MinDurationNano is { } minDuration)
        {
            parameters.AddParameter($"ts{letter}MinDuration", minDuration);
            clauses.Add($"DurationNano >= {{ts{letter}MinDuration:UInt64}}");
        }

        if (condition.MaxDurationNano is { } maxDuration)
        {
            parameters.AddParameter($"ts{letter}MaxDuration", maxDuration);
            clauses.Add($"DurationNano <= {{ts{letter}MaxDuration:UInt64}}");
        }

        if (condition.Attributes is { Count: > 0 } attributes)
        {
            for (var i = 0; i < attributes.Count; i++)
            {
                clauses.Add(SpanFilterSqlBuilder.AttributeClause(attributes[i], $"T{letter}_{i}", parameters, promoted));
            }
        }

        return $"({string.Join(" AND ", clauses)})";
    }
}
