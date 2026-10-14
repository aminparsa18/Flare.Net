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

    /// <summary>Null when nothing in the query is about a particular span (every trace in the window matches).</summary>
    public TraceStructureFilter? Structure => Conditions.Count == 0
        ? null
        : new TraceStructureFilter { Conditions = Conditions, Expression = Expression };
}

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
                throw new TraceQlException("an empty spanset { } can only be the whole query.");
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

        return Finish(conditions, conditions.Count == 0 ? null : query.Expression, trace);
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
        switch (field)
        {
            case "name":
                builder.SetSpanName(RequireEquality(predicate, "name"));
                return;
            case "status":
                builder.SetStatus(StatusLabel(predicate));
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
            case "kind" or "statusMessage" or "childCount" or "parent" or "nestedSetLeft" or "nestedSetRight" or "nestedSetParent" or "traceID" or "spanID":
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
        else if (field.StartsWith('.'))
        {
            var key = field[1..];
            var bag = ResourceNamespaces.Any(p => key.StartsWith(p, StringComparison.Ordinal)) ? SpanAttributeBag.Resource : SpanAttributeBag.Span;
            ApplyAttribute(predicate, key, bag, builder);
        }
        else
        {
            throw new TraceQlException($"'{field}' is not a field; scope attributes as span.{field}, resource.{field} or .{field}.");
        }
    }

    private static void ApplyAttribute(TraceQlPredicate predicate, string key, SpanAttributeBag bag, ConditionBuilder builder)
    {
        if (key.Length == 0)
        {
            throw new TraceQlException($"'{predicate.Field}' has no attribute name.");
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

    private static string RequireEquality(TraceQlPredicate predicate, string field)
    {
        if (predicate.Op != TraceQlOp.Eq)
        {
            throw new TraceQlException($"'{field}' supports only = in this subset (an exact match).");
        }

        return predicate.Value.Kind == TraceQlValueKind.String
            ? predicate.Value.Text
            : throw new TraceQlException($"{field} = needs a quoted string.");
    }

    private static string StatusLabel(TraceQlPredicate predicate)
    {
        if (predicate.Op != TraceQlOp.Eq)
        {
            throw new TraceQlException("'status' supports only = in this subset, e.g. status = error.");
        }

        var name = predicate.Value.Text;
        return name switch
        {
            "error" => "STATUS_CODE_ERROR",
            "ok" => "STATUS_CODE_OK",
            "unset" => "STATUS_CODE_UNSET",
            _ => throw new TraceQlException($"unknown status '{name}'; use error, ok or unset."),
        };
    }

    private static ulong DurationNanos(TraceQlPredicate predicate) => predicate.Value.Kind == TraceQlValueKind.Duration
        ? predicate.Value.Nanos
        : throw new TraceQlException($"{predicate.Field} must be compared with a duration such as 250ms or 2s.");

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
                trace.Names.Add(RequireEquality(predicate, "rootName"));
                break;
            case "rootServiceName":
                trace.Services.Add(RequireEquality(predicate, "rootServiceName"));
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
        private string? _spanName;
        private string? _status;

        public string? Service { get; set; }

        public DurationRange Duration { get; } = new();

        public List<SpanAttributeFilter> Attributes { get; } = [];

        public bool IsBlank =>
            Service is null && _spanName is null && _status is null && Duration.Min is null && Duration.Max is null && Attributes.Count == 0;

        public void SetSpanName(string name)
        {
            if (_spanName is not null && _spanName != name)
            {
                throw new TraceQlException("'name' is compared twice in one spanset; no span can match both.");
            }

            _spanName = name;
        }

        public void SetStatus(string label)
        {
            if (_status is not null && _status != label)
            {
                throw new TraceQlException("'status' is compared twice in one spanset; no span can match both.");
            }

            _status = label;
        }

        public TraceSpanCondition Build(string letter) => new()
        {
            Name = letter,
            ServiceName = Service,
            SpanName = _spanName,
            StatusCode = _status,
            MinDurationNano = Duration.Min,
            MaxDurationNano = Duration.Max,
            Attributes = Attributes.Count == 0 ? null : Attributes,
        };
    }
}
