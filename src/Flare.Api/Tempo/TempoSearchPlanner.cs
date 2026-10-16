using System.Globalization;
using System.Text;
using Flare.Api.Model;

namespace Flare.Api.Tempo;

/// <summary>
/// What a Tempo search resolves to in Flare terms: root-row (trace-level) restrictions, plus an
/// optional structural filter ("the trace has a span matching ...") for everything that is about a
/// span rather than the trace. See <see cref="TempoSearchPlanner"/>.
/// </summary>
public sealed record TempoSearchPlan
{
    public IReadOnlyList<TraceSpanCondition> Conditions { get; init; } = [];

    public string? Expression { get; init; }

    public IReadOnlyList<string> RootServices { get; init; } = [];

    public IReadOnlyList<string> RootNames { get; init; } = [];

    public ulong? MinTraceDurationNano { get; init; }

    public ulong? MaxTraceDurationNano { get; init; }

    /// <summary>What a <c>| select(...)</c> stage asked each listed span to carry, in the order written (ADR-0227).</summary>
    public IReadOnlyList<TempoSelectField> Select { get; init; } = [];

    /// <summary>The <c>&amp;&gt;&gt;</c> relations: for these the spans listed are only the ones that take part, on both sides.</summary>
    public IReadOnlyList<TraceQlUnionRelation> UnionDescendants { get; init; } = [];

    /// <summary>
    /// Whether the spans listed come from <c>TempoSpanSelector</c> rather than the plain matched-spans query: a <c>select()</c>
    /// stage or a union relation needs the per-span fields or the trace's span tree.
    /// </summary>
    public bool UsesSelector => Select.Count > 0 || UnionDescendants.Count > 0;

    /// <summary>
    /// Whether the selector needs every span of each trace (not only the ones matching a condition): to number the span tree
    /// (<c>nestedSetLeft</c> and friends) or to look for the ancestors of a union relation.
    /// </summary>
    public bool NeedsWholeTrace => UnionDescendants.Count > 0
        || Select.Any(f => f.Scope == TempoSelectScope.Intrinsic && f.Key.StartsWith("nestedSet", StringComparison.Ordinal));

    /// <summary>Null when nothing in the query is about a particular span (every trace in the window matches).</summary>
    public TraceStructureFilter? Structure => Conditions.Count == 0
        ? null
        : new TraceStructureFilter { Conditions = Conditions, Expression = Expression };
}

public enum TempoSelectScope
{
    /// <summary><c>name</c>, <c>status</c>, <c>kind</c>, <c>duration</c> and the <c>nestedSet*</c> numbers.</summary>
    Intrinsic,
    Span,
    Resource,

    /// <summary>An attribute of the span's events (<c>event.exception.message</c>).</summary>
    Event,
}

/// <param name="Key">The intrinsic's name, or the attribute key without its scope.</param>
public sealed record TempoSelectField(TempoSelectScope Scope, string Key);

/// <summary>
/// Compiles Tempo search input - a parsed TraceQL query, or the legacy <c>tags</c> logfmt - into a
/// <see cref="TempoSearchPlan"/>. Pure, no ClickHouse dependency.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tempo returns traces; Flare's span search returns spans.</b> A search is therefore a
/// <c>RootSpansOnly</c> list (one row per trace, carrying the root's service, name, start and duration)
/// narrowed by a <see cref="SpanFilter.Structure"/> subquery - the same trace-level "contains a span
/// like this" evaluation the Traces page's structure builder uses (ADR-0069). Each TraceQL spanset is
/// one structure condition and the spanset operators map onto its expression: <c>&amp;&amp;</c>,
/// <c>||</c>, <c>&gt;</c> (<c>-&gt;</c>, direct child) and <c>&gt;&gt;</c> (<c>=&gt;</c>, descendant).
/// </para>
/// <para>
/// <b>Trace-level intrinsics</b> (<c>rootName</c>, <c>rootServiceName</c>, <c>traceDuration</c>) filter
/// the root row instead of a condition, so they are accepted only in a query that is one spanset: in
/// <c>{ rootName = "a" } || { ... }</c> they would silently mean something else.
/// </para>
/// <para>
/// <b>Unscoped attributes</b> (<c>.service.name</c>, or a bare <c>tags</c> key) are Tempo's "span or
/// resource". Flare's attribute filters name one bag, so an unscoped key is read from the resource bag
/// when it sits in an OpenTelemetry resource namespace (<see cref="ResourceNamespaces"/>) and from the
/// span bag otherwise; write <c>span.x</c> or <c>resource.x</c> to be explicit.
/// </para>
/// </remarks>
public static class TempoSearchPlanner
{
    /// <summary>Attribute-name prefixes the OpenTelemetry semantic conventions define for resources.</summary>
    internal static readonly string[] ResourceNamespaces =
    [
        "service.", "k8s.", "host.", "cloud.", "deployment.", "process.", "telemetry.", "container.", "os.", "device.",
        "faas.", "webengine.", "cluster.", "aws.", "gcp.", "azure.",
    ];

    public static TempoSearchPlan Plan(TraceQlQuery query)
    {
        var traceLevelAllowed = query.Spansets.Count == 1;
        var trace = new TraceLevel();
        var conditions = new List<TraceSpanCondition>();
        for (var i = 0; i < query.Spansets.Count; i++)
        {
            var spanset = query.Spansets[i];
            if (spanset.Predicates.Count == 0 && query.Spansets.Count > 1)
            {
                // Next to a structural operator an empty spanset is "any span" (Traces Drilldown asks for { true } descendants);
                // combined with && or || it would change nothing or everything, so that stays an error.
                if (!IsRelationOperand(query.Expression, (char)('A' + i)))
                {
                    throw new TraceQlException("an empty spanset { } can only be the whole query, or a side of > , >> or &>>.");
                }

                conditions.Add(new TraceSpanCondition { Name = ((char)('A' + i)).ToString(), MinDurationNano = 0 });
                continue;
            }

            var builder = new ConditionBuilder();
            foreach (var predicate in spanset.Predicates)
            {
                Apply(predicate, builder, traceLevelAllowed ? trace : null);
            }

            if (builder.IsBlank)
            {
                continue;
            }

            conditions.Add(builder.Build(((char)('A' + i)).ToString()));
        }

        var plan = Finish(conditions, conditions.Count == 0 ? null : query.Expression, trace);
        return plan with { Select = query.Select.Select(SelectField).ToArray(), UnionDescendants = query.UnionDescendants };
    }

    /// <summary>Whether the letter stands directly beside <c>-&gt;</c> or <c>=&gt;</c> in the structural expression.</summary>
    private static bool IsRelationOperand(string expression, char letter) =>
        expression.Contains($"{letter} ->", StringComparison.Ordinal) || expression.Contains($"{letter} =>", StringComparison.Ordinal)
        || expression.Contains($"-> {letter}", StringComparison.Ordinal) || expression.Contains($"=> {letter}", StringComparison.Ordinal);

    /// <summary>A <c>select()</c> field as written, resolved like a filter's: an unscoped <c>.x</c> is a resource attribute in a resource namespace and a span attribute otherwise.</summary>
    internal static TempoSelectField SelectField(string field)
    {
        if (field.StartsWith("span.", StringComparison.Ordinal))
        {
            return new TempoSelectField(TempoSelectScope.Span, field["span.".Length..]);
        }

        if (field.StartsWith("resource.", StringComparison.Ordinal))
        {
            return new TempoSelectField(TempoSelectScope.Resource, field["resource.".Length..]);
        }

        if (field.StartsWith("event.", StringComparison.Ordinal))
        {
            return new TempoSelectField(TempoSelectScope.Event, field["event.".Length..]);
        }

        if (field.StartsWith('.'))
        {
            var key = field[1..];
            return new TempoSelectField(ResourceNamespaces.Any(p => key.StartsWith(p, StringComparison.Ordinal)) ? TempoSelectScope.Resource : TempoSelectScope.Span, key);
        }

        return new TempoSelectField(TempoSelectScope.Intrinsic, field);
    }

    /// <summary>
    /// Tempo's <c>tags</c> parameter: space-separated <c>key=value</c> pairs (logfmt, values optionally
    /// double-quoted), all of which must hold for one span of the trace.
    /// </summary>
    public static TempoSearchPlan PlanTags(string? tags)
    {
        var trace = new TraceLevel();
        var builder = new ConditionBuilder();
        foreach (var (key, value) in ParseLogfmt(tags))
        {
            var predicate = new TraceQlPredicate(TagField(key), TraceQlOp.Eq, new TraceQlValue(TraceQlValueKind.String, value));
            Apply(predicate, builder, trace);
        }

        var conditions = builder.IsBlank ? [] : new List<TraceSpanCondition> { builder.Build("A") };
        return Finish(conditions, conditions.Count == 0 ? null : "A", trace);
    }

    /// <summary>The Tempo <c>minDuration</c>/<c>maxDuration</c> parameters, which bound the trace (the root span here).</summary>
    public static TempoSearchPlan WithDurations(TempoSearchPlan plan, string? minDuration, string? maxDuration)
    {
        var min = ParseDurationParameter(minDuration, "minDuration");
        var max = ParseDurationParameter(maxDuration, "maxDuration");
        return plan with
        {
            MinTraceDurationNano = Tighter(plan.MinTraceDurationNano, min, takeLarger: true),
            MaxTraceDurationNano = Tighter(plan.MaxTraceDurationNano, max, takeLarger: false),
        };
    }

    /// <summary>
    /// A tag as the tag-value and tag-name APIs name it, resolved to the field <see cref="Apply"/>
    /// understands: a Tempo intrinsic or scoped name stays as is, an unscoped one gets its scope.
    /// </summary>
    internal static string TagField(string key) => key switch
    {
        "name" or "status" or "duration" or "rootName" or "rootServiceName" or "traceDuration" => key,
        "root.name" => "rootName",
        "root.service.name" => "rootServiceName",
        _ when key.StartsWith("span.", StringComparison.Ordinal) || key.StartsWith("resource.", StringComparison.Ordinal) => key,
        _ => "." + key,
    };

    internal static IReadOnlyList<(string Key, string Value)> ParseLogfmt(string? text)
    {
        var pairs = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return pairs;
        }

        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            var keyStart = i;
            while (i < text.Length && text[i] != '=' && !char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            if (i >= text.Length || text[i] != '=' || i == keyStart)
            {
                throw new TraceQlException($"tags must be key=value pairs; cannot read '{text[keyStart..i]}'.");
            }

            var key = text[keyStart..i];
            i++;
            var value = new StringBuilder();
            if (i < text.Length && text[i] == '"')
            {
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i++;
                    }

                    value.Append(text[i++]);
                }

                if (i >= text.Length)
                {
                    throw new TraceQlException($"the quoted value of tag '{key}' is not closed.");
                }

                i++;
            }
            else
            {
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                {
                    value.Append(text[i++]);
                }
            }

            pairs.Add((key, value.ToString()));
        }

        return pairs;
    }

    private static TempoSearchPlan Finish(List<TraceSpanCondition> conditions, string? expression, TraceLevel trace) => new()
    {
        Conditions = conditions,
        Expression = expression,
        RootServices = trace.Services,
        RootNames = trace.Names,
        MinTraceDurationNano = trace.Min,
        MaxTraceDurationNano = trace.Max,
    };

    private static ulong? ParseDurationParameter(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return TraceQlParser.TryParseDuration(text.Trim(), out var nanos)
            ? nanos
            : throw new TraceQlException($"invalid {name} '{text}': use a duration such as 250ms or 2s.");
    }

    private static ulong? Tighter(ulong? a, ulong? b, bool takeLarger) =>
        (a, b) switch
        {
            (null, _) => b,
            (_, null) => a,
            var (x, y) => takeLarger ? Math.Max(x!.Value, y!.Value) : Math.Min(x!.Value, y!.Value),
        };

    private static void Apply(TraceQlPredicate predicate, ConditionBuilder builder, TraceLevel? trace)
    {
        var field = predicate.Field;
        if (predicate.Alternatives is not null && field is "duration" or "nestedSetParent" or "nestedSetLeft" or "nestedSetRight"
            or "statusMessage" or "childCount" or "parent" or "event:timeSinceStart")
        {
            throw new TraceQlException($"a '||' group of values is supported for name, status, kind, rootName, rootServiceName, ids and attributes, not '{field}'.");
        }

        switch (field)
        {
            case "name":
                ApplyName(predicate, builder);
                return;
            case "status":
                ApplyStatus(predicate, builder);
                return;
            case "kind":
                ApplyKind(predicate, builder);
                return;
            case "duration":
                ApplyDuration(predicate, builder.Duration);
                return;
            case "rootName":
            case "rootServiceName":
            case "traceDuration":
                if (trace is null)
                {
                    throw new TraceQlException($"'{field}' describes the whole trace, so it can only be used when the query is a single spanset.");
                }

                ApplyTraceLevel(predicate, trace);
                return;
            case "nestedSetParent":
                ApplyNestedSetParent(predicate, builder);
                return;
            case "span:id":
                ApplyColumn(predicate, builder, "SpanId", IdLength.Span);
                return;
            case "span:parentID":
                ApplyColumn(predicate, builder, "ParentSpanId", IdLength.Span);
                return;
            case "trace:id":
                ApplyColumn(predicate, builder, "TraceId", IdLength.Trace);
                return;
            case "instrumentation:name":
                ApplyColumn(predicate, builder, "ScopeName", IdLength.None);
                return;
            case "instrumentation:version":
                ApplyColumn(predicate, builder, "ScopeVersion", IdLength.None);
                return;
            case "link:spanID":
                ApplyColumn(predicate, builder, "Links.SpanId", IdLength.Span);
                return;
            case "link:traceID":
                ApplyColumn(predicate, builder, "Links.TraceId", IdLength.Trace);
                return;
            case "event:timeSinceStart":
                ApplyEventTimeSinceStart(predicate, builder);
                return;
            case "statusMessage" or "childCount" or "parent" or "nestedSetLeft" or "nestedSetRight":
                throw new TraceQlException($"the intrinsic '{field}' is not supported.");
        }

        if (field.StartsWith("span.", StringComparison.Ordinal))
        {
            ApplyAttribute(predicate, field["span.".Length..], SpanAttributeBag.Span, builder);
        }
        else if (field.StartsWith("resource.", StringComparison.Ordinal))
        {
            ApplyAttribute(predicate, field["resource.".Length..], SpanAttributeBag.Resource, builder);
        }
        else if (field.StartsWith("event.", StringComparison.Ordinal))
        {
            ApplyAttribute(predicate, field["event.".Length..], SpanAttributeBag.Event, builder);
        }
        else if (field.StartsWith('.'))
        {
            var key = field[1..];
            var bag = ResourceNamespaces.Any(p => key.StartsWith(p, StringComparison.Ordinal)) ? SpanAttributeBag.Resource : SpanAttributeBag.Span;
            ApplyAttribute(predicate, key, bag, builder);
        }
        else if (field.Contains(':'))
        {
            // A scoped intrinsic Flare keeps no data for (span:statusMessage as a filter, ...).
            throw new TraceQlException($"the intrinsic '{field}' is not supported.");
        }
        else
        {
            throw new TraceQlException($"'{field}' is not a field; scope attributes as span.{field}, resource.{field} or .{field}.");
        }
    }

    /// <summary>How long a hexadecimal id is when written in full; shorter ones are zero-padded on the left, as Tempo reads them.</summary>
    private enum IdLength
    {
        None = 0,
        Span = 16,
        Trace = 32,
    }

    /// <summary>
    /// A test on a column of the span that TraceQL names with a scoped intrinsic (ADR-0229): the span, parent and trace ids, the
    /// instrumentation scope's name and version, and the ids of the span's links. Text compares with <c>=</c>, <c>!=</c>, <c>=~</c> and
    /// <c>!~</c> (or a <c>||</c> group of <c>=</c>); <c>= nil</c> and <c>!= nil</c> test for empty and non-empty. For a link the test holds
    /// when some link satisfies it. An id is lower-cased and left-padded with zeros before it is compared; a pattern is not touched.
    /// </summary>
    private static void ApplyColumn(TraceQlPredicate predicate, ConditionBuilder builder, string column, IdLength idLength)
    {
        string Id(TraceQlValue value) => NormalizeId(value.Kind == TraceQlValueKind.String
            ? value.Text
            : throw new TraceQlException($"{predicate.Field} {OperatorText(predicate.Op)} needs a quoted string."), idLength);

        SpanAttributeFilter Filter(SpanAttributeFilterOperator op, string value, IReadOnlyList<string>? values = null) =>
            new() { Bag = SpanAttributeBag.Intrinsic, Key = column, Value = value, Operator = op, Values = values };

        if (predicate.Value.Kind == TraceQlValueKind.Nil)
        {
            builder.Attributes.Add(Filter(predicate.Op switch
            {
                TraceQlOp.Eq => SpanAttributeFilterOperator.Absent,
                TraceQlOp.Neq => SpanAttributeFilterOperator.Exists,
                _ => throw new TraceQlException($"{predicate.Field} can only be compared with nil using = or !="),
            }, string.Empty));
            return;
        }

        if (predicate.Alternatives is { Count: > 0 } alternatives)
        {
            builder.Attributes.Add(Filter(SpanAttributeFilterOperator.In, string.Empty, alternatives.Prepend(predicate.Value).Select(Id).Distinct(StringComparer.Ordinal).ToList()));
            return;
        }

        switch (predicate.Op)
        {
            case TraceQlOp.Eq:
                builder.Attributes.Add(Filter(SpanAttributeFilterOperator.Equals, Id(predicate.Value)));
                return;
            case TraceQlOp.Neq:
                builder.Attributes.Add(Filter(SpanAttributeFilterOperator.NotEquals, Id(predicate.Value)));
                return;
            case TraceQlOp.Re:
                builder.Attributes.Add(Filter(SpanAttributeFilterOperator.Regex, Anchor(RegexText(predicate.Value, predicate))));
                return;
            case TraceQlOp.Nre:
                builder.Attributes.Add(Filter(SpanAttributeFilterOperator.NotRegex, Anchor(RegexText(predicate.Value, predicate))));
                return;
            default:
                throw new TraceQlException($"'{predicate.Field}' supports =, !=, =~ and !~.");
        }
    }

    private static string NormalizeId(string text, IdLength length) =>
        length != IdLength.None && text.Length <= (int)length && text.All(char.IsAsciiHexDigit)
            ? text.ToLowerInvariant().PadLeft((int)length, '0')
            : text;

    private static string OperatorText(TraceQlOp op) => op switch
    {
        TraceQlOp.Eq => "=",
        TraceQlOp.Neq => "!=",
        TraceQlOp.Re => "=~",
        TraceQlOp.Nre => "!~",
        TraceQlOp.Gt => ">",
        TraceQlOp.Gte => ">=",
        TraceQlOp.Lt => "<",
        _ => "<=",
    };

    /// <summary>
    /// <c>event:timeSinceStart</c> (ADR-0229): the span has an event at that offset from its start, compared with a duration (a plain number
    /// is nanoseconds, as for <c>duration</c>). Each test is made against the events on its own, so <c>&gt; 1ms &amp;&amp; &lt; 5ms</c> is satisfied by one event
    /// above 1 ms and another below 5 ms, as two <c>event.x</c> tests are.
    /// </summary>
    private static void ApplyEventTimeSinceStart(TraceQlPredicate predicate, ConditionBuilder builder)
    {
        var op = predicate.Op switch
        {
            TraceQlOp.Eq => SpanAttributeFilterOperator.Equals,
            TraceQlOp.Neq => SpanAttributeFilterOperator.NotEquals,
            TraceQlOp.Gt => SpanAttributeFilterOperator.GreaterThan,
            TraceQlOp.Gte => SpanAttributeFilterOperator.GreaterThanOrEqual,
            TraceQlOp.Lt => SpanAttributeFilterOperator.LessThan,
            TraceQlOp.Lte => SpanAttributeFilterOperator.LessThanOrEqual,
            _ => throw new TraceQlException("'event:timeSinceStart' supports =, !=, >, >=, < and <=."),
        };
        builder.Attributes.Add(new SpanAttributeFilter
        {
            Bag = SpanAttributeBag.Intrinsic,
            Key = Query.SpanFilterSqlBuilder.EventTimeSinceStartKey,
            Value = DurationNanos(predicate).ToString(CultureInfo.InvariantCulture),
            Operator = op,
        });
    }

    /// <summary>
    /// <c>nestedSetParent</c> is -1 for a root span and the parent's (positive) nested-set position for any other, so a test on it
    /// is a test for "is a root" and "has a parent": Grafana's Traces Drilldown selects root spans with <c>nestedSetParent&lt;0</c>.
    /// Only a comparison with a whole number is read.
    /// </summary>
    private static void ApplyNestedSetParent(TraceQlPredicate predicate, ConditionBuilder builder)
    {
        if (predicate.Value.Kind != TraceQlValueKind.Number || !double.TryParse(predicate.Value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var c) || c != Math.Floor(c))
        {
            throw new TraceQlException("'nestedSetParent' is compared with a whole number, e.g. nestedSetParent < 0 for a root span.");
        }

        bool Holds(double value) => predicate.Op switch
        {
            TraceQlOp.Eq => value == c,
            TraceQlOp.Neq => value != c,
            TraceQlOp.Gt => value > c,
            TraceQlOp.Gte => value >= c,
            TraceQlOp.Lt => value < c,
            TraceQlOp.Lte => value <= c,
            _ => throw new TraceQlException("'nestedSetParent' supports =, !=, >, >=, < and <=."),
        };

        // Any positive value is a possible parent position: the test holds for some span with a parent if it holds for one of 1 and a larger one.
        var nonRoot = Holds(1) || Holds(double.MaxValue) || (predicate.Op == TraceQlOp.Eq && c >= 1);
        builder.RestrictParent(Holds(-1), nonRoot, $"nestedSetParent {predicate.Op} {predicate.Value.Text}");
    }

    private static void ApplyAttribute(TraceQlPredicate predicate, string key, SpanAttributeBag bag, ConditionBuilder builder)
    {
        if (key.Length == 0)
        {
            throw new TraceQlException($"'{predicate.Field}' has no attribute name.");
        }

        // `!= nil` is "the attribute exists", `= nil` "it does not": Traces Drilldown selects the spans that carry a resource attribute this way.
        if (predicate.Value.Kind == TraceQlValueKind.Nil)
        {
            var presence = predicate.Op switch
            {
                TraceQlOp.Eq => SpanAttributeFilterOperator.Absent,
                TraceQlOp.Neq => SpanAttributeFilterOperator.Exists,
                _ => throw new TraceQlException($"{predicate.Field} can only be compared with nil using = or !=."),
            };
            builder.Attributes.Add(new SpanAttributeFilter { Bag = bag, Key = key, Value = string.Empty, Operator = presence });
            return;
        }

        // `(f = a || f = b)`, which Grafana's Search tab writes for several picked values (ADR-0228): a membership test.
        if (predicate.Alternatives is { Count: > 0 } alternatives)
        {
            var values = alternatives.Prepend(predicate.Value).Select(AttributeText).Distinct(StringComparer.Ordinal).ToList();
            if (bag == SpanAttributeBag.Resource && key == "service.name")
            {
                builder.RestrictServices(values);
                return;
            }

            builder.Attributes.Add(new SpanAttributeFilter { Bag = bag, Key = key, Value = string.Empty, Operator = SpanAttributeFilterOperator.In, Values = values });
            return;
        }

        // The service is a first-class column, which the structure filter matches exactly (and the ingest sort key favours).
        if (bag == SpanAttributeBag.Resource && key == "service.name" && predicate.Op == TraceQlOp.Eq
            && predicate.Value.Kind == TraceQlValueKind.String && builder.Service is null)
        {
            builder.Service = predicate.Value.Text;
            return;
        }

        var value = predicate.Value;
        var (op, text) = predicate.Op switch
        {
            TraceQlOp.Eq => (SpanAttributeFilterOperator.Equals, AttributeText(value)),
            TraceQlOp.Neq => (SpanAttributeFilterOperator.NotEquals, AttributeText(value)),
            // Tempo anchors regular expressions; ClickHouse's match() does not.
            TraceQlOp.Re => (SpanAttributeFilterOperator.Regex, Anchor(RegexText(value, predicate))),
            TraceQlOp.Nre => (SpanAttributeFilterOperator.NotRegex, Anchor(RegexText(value, predicate))),
            TraceQlOp.Gt => (SpanAttributeFilterOperator.GreaterThan, NumericText(value, predicate)),
            TraceQlOp.Gte => (SpanAttributeFilterOperator.GreaterThanOrEqual, NumericText(value, predicate)),
            TraceQlOp.Lt => (SpanAttributeFilterOperator.LessThan, NumericText(value, predicate)),
            _ => (SpanAttributeFilterOperator.LessThanOrEqual, NumericText(value, predicate)),
        };
        builder.Attributes.Add(new SpanAttributeFilter { Bag = bag, Key = key, Value = text, Operator = op });
    }

    private static string AttributeText(TraceQlValue value) => value.Kind == TraceQlValueKind.Duration
        ? throw new TraceQlException($"cannot compare an attribute with the duration {value.Text}; attributes hold plain values.")
        : value.Text;

    private static string RegexText(TraceQlValue value, TraceQlPredicate predicate) => value.Kind == TraceQlValueKind.String
        ? value.Text
        : throw new TraceQlException($"{predicate.Field} {(predicate.Op == TraceQlOp.Re ? "=~" : "!~")} needs a quoted pattern.");

    private static string Anchor(string pattern) => $"^(?:{pattern})$";

    private static string NumericText(TraceQlValue value, TraceQlPredicate predicate) => value.Kind == TraceQlValueKind.Number
        ? value.Text
        : throw new TraceQlException($"{predicate.Field} can only be compared with <, <=, > or >= against a number.");

    /// <summary>The exact strings a <c>rootName = ...</c> (or a group of them) tests for.</summary>
    private static IEnumerable<string> RequireEquality(TraceQlPredicate predicate, string field)
    {
        if (predicate.Op != TraceQlOp.Eq)
        {
            throw new TraceQlException($"'{field}' supports only = in this subset (an exact match).");
        }

        foreach (var value in AllValues(predicate))
        {
            yield return value.Kind == TraceQlValueKind.String
                ? value.Text
                : throw new TraceQlException($"{field} = needs a quoted string.");
        }
    }

    private static IEnumerable<TraceQlValue> AllValues(TraceQlPredicate predicate) =>
        predicate.Alternatives is null ? [predicate.Value] : predicate.Alternatives.Prepend(predicate.Value);

    private static void ApplyName(TraceQlPredicate predicate, ConditionBuilder builder)
    {
        switch (predicate.Op)
        {
            case TraceQlOp.Eq:
                builder.NameIn(AllValues(predicate).Select(v => RequireString(predicate, v)).ToList());
                return;
            case TraceQlOp.Neq:
                builder.NameNotIn(RequireString(predicate, predicate.Value));
                return;
            // Tempo anchors regular expressions; ClickHouse's match() does not.
            case TraceQlOp.Re:
                builder.SetSpanNamePattern(Anchor(RegexText(predicate.Value, predicate)), SpanNameMatch.Regex);
                return;
            case TraceQlOp.Nre:
                builder.SetSpanNamePattern(Anchor(RegexText(predicate.Value, predicate)), SpanNameMatch.NotRegex);
                return;
            default:
                throw new TraceQlException("'name' supports =, !=, =~ and !~.");
        }
    }

    private static string RequireString(TraceQlPredicate predicate, TraceQlValue value) => value.Kind == TraceQlValueKind.String
        ? value.Text
        : throw new TraceQlException($"{predicate.Field} {(predicate.Op == TraceQlOp.Eq ? "=" : "!=")} needs a quoted string.");

    /// <summary>RE2 metacharacters, escaped so a literal value can be one branch of an alternation.</summary>
    private static string RegexQuote(string literal)
    {
        var quoted = new StringBuilder(literal.Length + 4);
        foreach (var c in literal)
        {
            if (@"\.+*?()|[]{}^$".Contains(c))
            {
                quoted.Append('\\');
            }

            quoted.Append(c);
        }

        return quoted.ToString();
    }

    private static void ApplyStatus(TraceQlPredicate predicate, ConditionBuilder builder)
    {
        switch (predicate.Op)
        {
            case TraceQlOp.Eq:
                builder.RestrictStatus(AllValues(predicate).Select(StatusLabel).ToList());
                return;
            case TraceQlOp.Neq:
                builder.ExcludeStatus(StatusLabel(predicate.Value));
                return;
            default:
                throw new TraceQlException("'status' supports = and !=, e.g. status = error.");
        }
    }

    /// <summary>OTel <c>SpanKind</c> numbers.</summary>
    private static byte KindNumber(TraceQlValue value) => value.Kind == TraceQlValueKind.Kind
        ? value.Text switch
        {
            "unspecified" => (byte)0,
            "internal" => (byte)1,
            "server" => (byte)2,
            "client" => (byte)3,
            "producer" => (byte)4,
            _ => (byte)5,
        }
        : throw new TraceQlException("'kind' is compared with unspecified, internal, server, client, producer or consumer.");

    private static void ApplyKind(TraceQlPredicate predicate, ConditionBuilder builder)
    {
        switch (predicate.Op)
        {
            case TraceQlOp.Eq:
                builder.RestrictKind(AllValues(predicate).Select(KindNumber).ToList());
                return;
            case TraceQlOp.Neq:
                builder.ExcludeKind(KindNumber(predicate.Value));
                return;
            default:
                throw new TraceQlException("'kind' supports = and !=, e.g. kind = server.");
        }
    }

    private static string StatusLabel(TraceQlValue value)
    {
        var name = value.Text;
        return name switch
        {
            "error" => "STATUS_CODE_ERROR",
            "ok" => "STATUS_CODE_OK",
            "unset" => "STATUS_CODE_UNSET",
            _ => throw new TraceQlException($"unknown status '{name}'; use error, ok or unset."),
        };
    }

    /// <summary>A duration literal, or a plain number of nanoseconds: Tempo compares a duration with any number, and Traces Drilldown writes <c>duration &gt;= 0</c>.</summary>
    private static ulong DurationNanos(TraceQlPredicate predicate)
    {
        var value = predicate.Value;
        if (value.Kind == TraceQlValueKind.Duration)
        {
            return value.Nanos;
        }

        if (value.Kind == TraceQlValueKind.Number && double.TryParse(value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var nanos) && nanos >= 0 && nanos < ulong.MaxValue)
        {
            return (ulong)Math.Round(nanos);
        }

        throw new TraceQlException($"{predicate.Field} must be compared with a duration such as 250ms or 2s (a plain number is read as nanoseconds).");
    }

    private static void ApplyDuration(TraceQlPredicate predicate, DurationRange range)
    {
        var nanos = DurationNanos(predicate);
        switch (predicate.Op)
        {
            case TraceQlOp.Eq:
                range.Raise(nanos);
                range.Lower(nanos);
                break;
            case TraceQlOp.Gte:
                range.Raise(nanos);
                break;
            case TraceQlOp.Gt:
                // Nanosecond integers, so "greater than" is "at least one more".
                range.Raise(nanos == ulong.MaxValue ? nanos : nanos + 1);
                break;
            case TraceQlOp.Lte:
                range.Lower(nanos);
                break;
            case TraceQlOp.Lt:
                range.Lower(nanos == 0 ? 0 : nanos - 1);
                break;
            default:
                throw new TraceQlException($"'{predicate.Field}' supports =, >, >=, < and <=.");
        }
    }

    private static void ApplyTraceLevel(TraceQlPredicate predicate, TraceLevel trace)
    {
        switch (predicate.Field)
        {
            case "rootName":
                trace.Names.AddRange(RequireEquality(predicate, "rootName"));
                break;
            case "rootServiceName":
                trace.Services.AddRange(RequireEquality(predicate, "rootServiceName"));
                break;
            default:
                var range = new DurationRange();
                ApplyDuration(predicate, range);
                trace.Min = Tighter(trace.Min, range.Min, takeLarger: true);
                trace.Max = Tighter(trace.Max, range.Max, takeLarger: false);
                break;
        }
    }

    private sealed class DurationRange
    {
        public ulong? Min { get; private set; }

        public ulong? Max { get; private set; }

        public void Raise(ulong nanos) => Min = Min is { } m ? Math.Max(m, nanos) : nanos;

        public void Lower(ulong nanos) => Max = Max is { } m ? Math.Min(m, nanos) : nanos;
    }

    private sealed class TraceLevel
    {
        public List<string> Services { get; } = [];

        public List<string> Names { get; } = [];

        public ulong? Min { get; set; }

        public ulong? Max { get; set; }
    }

    private sealed class ConditionBuilder
    {
        private static readonly string[] AllStatuses = ["STATUS_CODE_UNSET", "STATUS_CODE_OK", "STATUS_CODE_ERROR"];
        private static readonly byte[] AllKinds = [0, 1, 2, 3, 4, 5];

        // A span name is either one pattern (=~ / !~) or sets of exact names: some `=` (their intersection) minus every `!=`.
        private string? _spanName;
        private SpanNameMatch _spanNameMatch;
        private List<string>? _nameIn;
        private readonly List<string> _nameNotIn = [];

        // Status and kind are small enumerations, so `=`, `!=` and groups of them narrow a set of the allowed values.
        private List<string>? _statuses;
        private List<byte>? _kinds;
        private List<string>? _services;

        public string? Service { get; set; }

        public DurationRange Duration { get; } = new();

        public List<SpanAttributeFilter> Attributes { get; } = [];

        private bool _rootAllowed = true;
        private bool _nonRootAllowed = true;

        /// <summary>Root only, non-root only, or null for either, from the <c>nestedSetParent</c> tests of the spanset.</summary>
        public bool? RootSpan => _rootAllowed == _nonRootAllowed ? null : _rootAllowed;

        public bool IsBlank =>
            Service is null && _services is null && ResolveName().Text is null && ResolveStatus().Label is null && ResolveKind() is { Kind: null, Set: null }
            && RootSpan is null && Duration.Min is null && Duration.Max is null && Attributes.Count == 0;

        /// <summary>Narrows to the spans a <c>nestedSetParent</c> test lets through; one that no span passes is an error rather than an empty result.</summary>
        public void RestrictParent(bool rootMatches, bool nonRootMatches, string text)
        {
            _rootAllowed &= rootMatches;
            _nonRootAllowed &= nonRootMatches;
            if (!_rootAllowed && !_nonRootAllowed)
            {
                throw new TraceQlException($"no span satisfies '{text}'; nestedSetParent is -1 for a root span and positive for any other.");
            }
        }

        public void SetSpanNamePattern(string pattern, SpanNameMatch match)
        {
            if (_spanName is not null && (_spanName != pattern || _spanNameMatch != match))
            {
                throw new TraceQlException("'name' is compared twice in one spanset; no single span name test covers both.");
            }

            _spanName = pattern;
            _spanNameMatch = match;
        }

        public void NameIn(List<string> names) => _nameIn = Narrow(_nameIn, names);

        public void NameNotIn(string name) => _nameNotIn.Add(name);

        public void RestrictServices(List<string> services) => _services = Narrow(_services, services);

        public void RestrictStatus(List<string> labels) => _statuses = Narrow(_statuses, labels);

        public void ExcludeStatus(string label) => _statuses = (_statuses ?? [.. AllStatuses]).Where(l => l != label).ToList();

        public void RestrictKind(List<byte> kinds) => _kinds = Narrow(_kinds, kinds);

        public void ExcludeKind(byte kind) => _kinds = (_kinds ?? [.. AllKinds]).Where(k => k != kind).ToList();

        private static List<T> Narrow<T>(List<T>? current, List<T> values) =>
            current is null ? values.Distinct().ToList() : current.Intersect(values).ToList();

        private (string? Text, SpanNameMatch Match) ResolveName()
        {
            if (_spanName is not null)
            {
                return _nameIn is null && _nameNotIn.Count == 0
                    ? (_spanName, _spanNameMatch)
                    : throw new TraceQlException("'name' is compared twice in one spanset; no single span name test covers both.");
            }

            if (_nameIn is not null)
            {
                var names = _nameIn.Where(n => !_nameNotIn.Contains(n)).ToList();
                return names.Count switch
                {
                    0 => throw new TraceQlException("no span name satisfies the 'name' tests of this spanset."),
                    1 => (names[0], SpanNameMatch.Equals),
                    _ => (Anchor(string.Join('|', names.Select(RegexQuote))), SpanNameMatch.Regex),
                };
            }

            var excluded = _nameNotIn.Distinct().ToList();
            return excluded.Count switch
            {
                0 => (null, default),
                1 => (excluded[0], SpanNameMatch.NotEquals),
                _ => (Anchor(string.Join('|', excluded.Select(RegexQuote))), SpanNameMatch.NotRegex),
            };
        }

        /// <summary>One label, negated when two of the three remain (<c>status = error || status = ok</c> is <c>status != unset</c>); none for no test.</summary>
        private (string? Label, bool Negated) ResolveStatus()
        {
            if (_statuses is null)
            {
                return (null, false);
            }

            return _statuses.Count switch
            {
                0 => throw new TraceQlException("no span satisfies the 'status' tests of this spanset."),
                1 => (_statuses[0], false),
                2 => (AllStatuses.First(l => !_statuses.Contains(l)), true),
                _ => (null, false),
            };
        }

        /// <summary>One kind, all but one (negated), a set of two to four (ADR-0229), or none for no test.</summary>
        private (byte? Kind, bool Negated, IReadOnlyList<byte>? Set) ResolveKind()
        {
            if (_kinds is null)
            {
                return (null, false, null);
            }

            return _kinds.Count switch
            {
                0 => throw new TraceQlException("no span satisfies the 'kind' tests of this spanset."),
                1 => (_kinds[0], false, null),
                5 => (AllKinds.First(k => !_kinds.Contains(k)), true, null),
                6 => (null, false, null),
                _ => (null, false, _kinds.Order().ToArray()),
            };
        }

        public TraceSpanCondition Build(string letter)
        {
            var (name, nameMatch) = ResolveName();
            var (status, statusNegated) = ResolveStatus();
            var (kind, kindNegated, kindSet) = ResolveKind();
            var service = Service;
            var services = _services;
            if (services is not null)
            {
                if (service is not null)
                {
                    // An exact service and a list of them: the list only matters if it admits that service.
                    services = services.Contains(service) ? null : [];
                }
                else if (services.Count == 1)
                {
                    service = services[0];
                    services = null;
                }

                if (services is { Count: 0 })
                {
                    throw new TraceQlException("no span satisfies the 'resource.service.name' tests of this spanset.");
                }
            }

            return new()
            {
                Name = letter,
                ServiceName = service,
                ServiceNames = services,
                SpanName = name,
                StatusCode = status,
                StatusCodeNegated = statusNegated,
                SpanNameMatch = nameMatch,
                Kind = kind,
                KindNegated = kindNegated,
                Kinds = kindSet,
                RootSpan = RootSpan,
                MinDurationNano = Duration.Min,
                MaxDurationNano = Duration.Max,
                Attributes = Attributes.Count == 0 ? null : Attributes,
            };
        }
    }
}
