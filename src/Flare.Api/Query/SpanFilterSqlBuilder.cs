using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>A parameterized <c>WHERE</c> fragment (no leading <c>WHERE</c> keyword) plus its bound parameters.</summary>
public sealed record SpanFilterSql(string WhereSql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="SpanFilter"/> → parameterized <c>WHERE</c>-clause translation, shared
/// by <see cref="SpanSearchQueryBuilder"/>. Deliberately has no ClickHouse connection
/// dependency, same "pure function, unit-testable on its own" style as
/// <see cref="LogFilterSqlBuilder"/> - not a reuse of it, since <see cref="SpanFilter"/>
/// is its own type with genuinely different fields.
/// </summary>
/// <remarks>
/// Same parameter-binding discipline as <see cref="LogFilterSqlBuilder"/>: every
/// request-supplied value is bound via ClickHouse.Driver's <c>{name:Type}</c> parameter
/// placeholders. The only thing ever string-interpolated directly into the SQL text is
/// the attribute bag's *column name* in <see cref="ColumnFor"/>, and that only ever
/// comes from the closed <see cref="SpanAttributeBag"/> enum.
/// </remarks>
public static class SpanFilterSqlBuilder
{
    /// <summary>Default lookback applied when <see cref="SpanFilter.From"/> is omitted. Same rationale as <see cref="LogFilterSqlBuilder.DefaultLookback"/>.</summary>
    public static readonly TimeSpan DefaultLookback = TimeSpan.FromHours(1);

    /// <param name="promoted">
    /// Promoted <c>spans</c> attribute columns (ADR-0063) to read instead of a map lookup -
    /// null/empty keeps every attribute filter on its <c>Map</c> column. Never changes which
    /// rows match, only how fast.
    /// </param>
    public static SpanFilterSql Build(SpanFilter filter, DateTimeOffset now, PromotedAttributeColumns? promoted = null)
    {
        var parameters = new ClickHouseParameterCollection();
        var clauses = new List<string>();

        var from = filter.From ?? now - DefaultLookback;
        var to = filter.To ?? now;
        parameters.AddParameter("from", from.UtcDateTime);
        parameters.AddParameter("to", to.UtcDateTime);
        clauses.Add("StartTime >= {from:DateTime64(9)}");
        clauses.Add("StartTime < {to:DateTime64(9)}");

        if (filter.Services is { Count: > 0 } services)
        {
            parameters.AddParameter("services", services.ToArray());
            clauses.Add("ServiceName IN {services:Array(String)}");
        }

        ServiceScope.Append(clauses, parameters);

        if (filter.Kinds is { Count: > 0 } kinds)
        {
            parameters.AddParameter("kinds", kinds.ToArray());
            clauses.Add("Kind IN {kinds:Array(UInt8)}");
        }

        if (filter.StatusCodes is { Count: > 0 } statusCodes)
        {
            parameters.AddParameter("statusCodes", statusCodes.ToArray());
            clauses.Add("StatusCode IN {statusCodes:Array(String)}");
        }

        if (filter.Names is { Count: > 0 } names)
        {
            parameters.AddParameter("names", names.ToArray());
            clauses.Add("Name IN {names:Array(String)}");
        }

        if (!string.IsNullOrEmpty(filter.TraceId))
        {
            parameters.AddParameter("traceId", filter.TraceId);
            clauses.Add("TraceId = {traceId:String}");
        }

        if (filter.RootSpansOnly)
        {
            clauses.Add("ParentSpanId = ''");
        }

        if (filter.EntrySpansOnly)
        {
            clauses.Add(EntrySpanClause(from, to, filter.Services is { Count: > 0 }, parameters));
        }

        if (filter.MinDurationNano is { } minDuration)
        {
            parameters.AddParameter("minDuration", minDuration);
            clauses.Add("DurationNano >= {minDuration:UInt64}");
        }

        if (filter.MaxDurationNano is { } maxDuration)
        {
            parameters.AddParameter("maxDuration", maxDuration);
            clauses.Add("DurationNano <= {maxDuration:UInt64}");
        }

        if (filter.Attributes is { Count: > 0 } attributes)
        {
            for (var i = 0; i < attributes.Count; i++)
            {
                clauses.Add(AttributeClause(attributes[i], i.ToString(System.Globalization.CultureInfo.InvariantCulture), parameters, promoted));
            }
        }

        // GLOBAL for the same build-the-set-once reason as EntrySpanClause.
        if (filter.Structure is { } structure)
        {
            clauses.Add($"TraceId GLOBAL IN ({TraceStructureSqlBuilder.BuildTraceIdQuery(structure, parameters, promoted)})");
        }

        if (filter.NPlusOneOnly)
        {
            clauses.Add($"TraceId GLOBAL IN ({NPlusOneQueryBuilder.BuildTraceIdQuery(parameters)})");
        }

        return new SpanFilterSql(string.Join(" AND ", clauses), parameters);
    }

    /// <summary>
    /// How far before the window's <c>from</c> a same-service parent may have started and
    /// still disqualify its child as an entry span - same value and rationale as
    /// <see cref="ServiceDependencyQueryBuilder.ParentStartSlack"/>.
    /// </summary>
    public static readonly TimeSpan EntryParentStartSlack = ServiceDependencyQueryBuilder.ParentStartSlack;

    /// <summary>
    /// <see cref="SpanFilter.EntrySpansOnly"/>'s clause: no parent, or no span in the
    /// (slack-widened) window with the parent's <c>(TraceId, SpanId)</c> <em>and</em> this
    /// span's own <c>ServiceName</c>. The 3-tuple <c>NOT IN</c> is the uncorrelated form of
    /// "the parent belongs to a different service" - the same self-join
    /// <see cref="ServiceDependencyQueryBuilder"/>'s edges query does, reduced to a set
    /// membership test. A parent that was never ingested (partial instrumentation, sampled
    /// out) counts as "different service": the span is where Flare first sees that request.
    /// </summary>
    /// <remarks>
    /// Evaluated at query time, not pre-computed at flush time: a child and its parent are
    /// routinely flushed in different batches (the parent ends, and is exported, last), so
    /// the flush worker can't see the parent's service. The subquery's <c>StartTime</c>
    /// bound lets it use migration 0025's <c>spans_by_start_time</c> projection (it only
    /// touches <c>TraceId</c>/<c>SpanId</c>/<c>ServiceName</c>/<c>StartTime</c>) - the cost
    /// is a second pass over the window, not the table. A <see cref="SpanFilter.Services"/>
    /// filter narrows the subquery too, since a disqualifying parent must share the child's
    /// service. <c>GLOBAL</c> so a cluster-mode <c>Distributed</c> <c>spans</c> builds the set
    /// once rather than per shard (a harmless no-op on a single node). Accepted gap: a
    /// same-service parent that started more than <see cref="EntryParentStartSlack"/> before
    /// the window leaves its child counted as an entry span.
    /// </remarks>
    private static string EntrySpanClause(DateTimeOffset from, DateTimeOffset to, bool hasServices, ClickHouseParameterCollection parameters)
    {
        parameters.AddParameter("entryParentFrom", (from - EntryParentStartSlack).UtcDateTime);
        var parentClauses = "StartTime >= {entryParentFrom:DateTime64(9)} AND StartTime < {to:DateTime64(9)}" +
            (hasServices ? " AND ServiceName IN {services:Array(String)}" : string.Empty);
        return "(ParentSpanId = '' OR (TraceId, ParentSpanId, ServiceName) GLOBAL NOT IN " +
            $"(SELECT TraceId, SpanId, ServiceName FROM spans WHERE {parentClauses}))";
    }

    /// <summary>
    /// One <see cref="SpanAttributeFilter"/>'s clause - same shape as
    /// <see cref="LogFilterSqlBuilder"/>'s <c>AttributeClause</c>: <c>Exists</c>/<c>Absent</c>
    /// compile to <c>mapContains</c> alone (<see cref="SpanAttributeFilter.Value"/> unused),
    /// and <c>NotEquals</c> guards with <c>mapContains</c> explicitly so a missing key
    /// counts as "not equal", not just relying on the map's empty-string default.
    /// <c>Regex</c>/<c>NotRegex</c> compile to ClickHouse's <c>match()</c> (RE2 syntax)
    /// instead of <c>=</c>, guarded by <c>mapContains</c> the same way. <c>In</c>/<c>NotIn</c>
    /// compile to <c>IN</c> against <see cref="SpanAttributeFilter.Values"/> (bound as an
    /// <c>Array(String)</c> parameter, empty when <c>Values</c> is null) instead of <c>=</c>
    /// against <c>Value</c>, guarded the same way too. <paramref name="suffix"/> makes the
    /// bound parameter names unique - <c>internal</c> so <see cref="TraceFunnelQueryBuilder"/>
    /// and <see cref="TraceStructureSqlBuilder"/> can compile several steps' or conditions'
    /// attribute filters into one query.
    /// </summary>
    internal static string AttributeClause(SpanAttributeFilter attribute, string suffix, ClickHouseParameterCollection parameters, PromotedAttributeColumns? promoted)
    {
        if (attribute.Bag == SpanAttributeBag.Event)
        {
            return EventClause(attribute, suffix, parameters);
        }

        if (attribute.Bag == SpanAttributeBag.Intrinsic)
        {
            return IntrinsicClause(attribute, suffix, parameters);
        }

        // Old/new semconv spellings of one attribute count as the same key: a positive
        // operator matches if any spelling does, a negative one only if none does.
        if (attribute.Bag == SpanAttributeBag.Span && SemconvAliases.Group(attribute.Key) is { } group)
        {
            var parts = group
                .Select((key, i) => SingleKeyClause(attribute with { Key = key }, $"{suffix}x{i}", parameters, promoted))
                .ToList();
            var negative = attribute.Operator is SpanAttributeFilterOperator.NotEquals or SpanAttributeFilterOperator.NotRegex
                or SpanAttributeFilterOperator.NotIn or SpanAttributeFilterOperator.Absent;
            return $"({string.Join(negative ? " AND " : " OR ", parts)})";
        }

        return SingleKeyClause(attribute, suffix, parameters, promoted);
    }

    /// <summary>
    /// An event attribute (ADR-0227): the span matches when some event of it does, so the ordinary clause is evaluated against each
    /// event's attribute map (<c>m</c>) inside <c>arrayExists</c>. <c>Absent</c> is "no event has the key", the negation of that, so a
    /// span without events is a span without the attribute. Event attributes are never promoted to columns.
    /// </summary>
    private static string EventClause(SpanAttributeFilter attribute, string suffix, ClickHouseParameterCollection parameters)
    {
        const string Events = "`Events.Attributes`";
        if (attribute.Operator == SpanAttributeFilterOperator.Absent)
        {
            var present = SingleKeyClause(attribute with { Operator = SpanAttributeFilterOperator.Exists }, suffix, parameters, null, "m");
            return $"NOT arrayExists(m -> {present}, {Events})";
        }

        return $"arrayExists(m -> {SingleKeyClause(attribute, suffix, parameters, null, "m")}, {Events})";
    }

    /// <summary>
    /// The span columns an <see cref="SpanAttributeBag.Intrinsic"/> filter may name, and what each is (ADR-0229; the status message and the
    /// names of the span's events, ADR-0233).
    /// </summary>
    private static readonly Dictionary<string, (string Expression, bool IsArray)> IntrinsicColumns = new(StringComparer.Ordinal)
    {
        ["SpanId"] = ("SpanId", false),
        ["ParentSpanId"] = ("ParentSpanId", false),
        ["TraceId"] = ("TraceId", false),
        ["ScopeName"] = ("ScopeName", false),
        ["ScopeVersion"] = ("ScopeVersion", false),
        ["StatusMessage"] = ("StatusMessage", false),
        ["Events.Name"] = ("`Events.Name`", true),
        ["Links.SpanId"] = ("`Links.SpanId`", true),
        ["Links.TraceId"] = ("`Links.TraceId`", true),
    };

    /// <summary>The key of an intrinsic filter on the time from a span's start to one of its events, in nanoseconds.</summary>
    internal const string EventTimeSinceStartKey = "Events.TimeSinceStart";

    /// <summary>Whether <paramref name="key"/> is one of the columns an <see cref="SpanAttributeBag.Intrinsic"/> filter can name.</summary>
    internal static bool IsIntrinsicKey(string key) => IntrinsicColumns.ContainsKey(key) || key == EventTimeSinceStartKey;

    /// <summary>
    /// A filter on a column of the span rather than an attribute map (ADR-0229). A string column takes every operator a string
    /// attribute does, <c>Exists</c> and <c>Absent</c> meaning non-empty and empty; an array column (the span's links) is true when some
    /// element satisfies it, and <c>Absent</c> when none is set. The time since start compares each event's offset in nanoseconds.
    /// </summary>
    private static string IntrinsicClause(SpanAttributeFilter attribute, string suffix, ClickHouseParameterCollection parameters)
    {
        if (attribute.Key == EventTimeSinceStartKey)
        {
            return EventTimeClause(attribute, suffix, parameters);
        }

        if (!IntrinsicColumns.TryGetValue(attribute.Key, out var column))
        {
            throw new ArgumentException($"'{attribute.Key}' is not a span column that can be filtered.");
        }

        if (!column.IsArray)
        {
            return ColumnValueClause(attribute, column.Expression, suffix, parameters);
        }

        var element = ColumnValueClause(attribute with { Operator = attribute.Operator == SpanAttributeFilterOperator.Absent ? SpanAttributeFilterOperator.Exists : attribute.Operator }, "l", suffix, parameters);
        var any = $"arrayExists(l -> {element}, {column.Expression})";
        return attribute.Operator == SpanAttributeFilterOperator.Absent ? $"NOT {any}" : any;
    }

    private static string ColumnValueClause(SpanAttributeFilter attribute, string expression, string suffix, ClickHouseParameterCollection parameters)
    {
        var valueParam = $"attrValue{suffix}";
        var valuesParam = $"attrValues{suffix}";
        switch (attribute.Operator)
        {
            case SpanAttributeFilterOperator.Exists:
                return $"{expression} != ''";
            case SpanAttributeFilterOperator.Absent:
                return $"{expression} = ''";
            case SpanAttributeFilterOperator.NotEquals:
                parameters.AddParameter(valueParam, attribute.Value);
                return $"{expression} != {{{valueParam}:String}}";
            case SpanAttributeFilterOperator.Regex:
                parameters.AddParameter(valueParam, attribute.Value);
                return $"match({expression}, {{{valueParam}:String}})";
            case SpanAttributeFilterOperator.NotRegex:
                parameters.AddParameter(valueParam, attribute.Value);
                return $"NOT match({expression}, {{{valueParam}:String}})";
            case SpanAttributeFilterOperator.In:
                parameters.AddParameter(valuesParam, (attribute.Values ?? []).ToArray());
                return $"{expression} IN {{{valuesParam}:Array(String)}}";
            case SpanAttributeFilterOperator.NotIn:
                parameters.AddParameter(valuesParam, (attribute.Values ?? []).ToArray());
                return $"{expression} NOT IN {{{valuesParam}:Array(String)}}";
            case SpanAttributeFilterOperator.Equals:
                parameters.AddParameter(valueParam, attribute.Value);
                return $"{expression} = {{{valueParam}:String}}";
            default:
                throw new ArgumentException($"'{attribute.Key}' is text; it supports =, !=, =~ and !~, not an ordering.");
        }
    }

    private static string EventTimeClause(SpanAttributeFilter attribute, string suffix, ClickHouseParameterCollection parameters)
    {
        const string Events = "`Events.TimeUnixNano`";
        switch (attribute.Operator)
        {
            case SpanAttributeFilterOperator.Exists:
                return $"notEmpty({Events})";
            case SpanAttributeFilterOperator.Absent:
                return $"empty({Events})";
        }

        var symbol = attribute.Operator switch
        {
            SpanAttributeFilterOperator.Equals => "=",
            SpanAttributeFilterOperator.NotEquals => "!=",
            SpanAttributeFilterOperator.GreaterThan => ">",
            SpanAttributeFilterOperator.GreaterThanOrEqual => ">=",
            SpanAttributeFilterOperator.LessThan => "<",
            SpanAttributeFilterOperator.LessThanOrEqual => "<=",
            _ => throw new ArgumentException("The time since the span's start is a duration; it supports =, !=, >, >=, < and <=."),
        };
        if (!long.TryParse(attribute.Value, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var nanos))
        {
            throw new ArgumentException("The time since the span's start is compared with a whole number of nanoseconds.");
        }

        var valueParam = $"attrValue{suffix}";
        parameters.AddParameter(valueParam, nanos);
        return $"arrayExists(t -> toUnixTimestamp64Nano(t) - toUnixTimestamp64Nano(StartTime) {symbol} {{{valueParam}:Int64}}, {Events})";
    }

    private static string SingleKeyClause(SpanAttributeFilter attribute, string suffix, ClickHouseParameterCollection parameters, PromotedAttributeColumns? promoted, string? columnOverride = null)
    {
        if (promoted is not null && promoted.TryGetColumn(PromotedBag(attribute.Bag), attribute.Key, out var promotedColumn)
            && PromotedClause(attribute, suffix, parameters, promotedColumn) is { } promotedSql)
        {
            return promotedSql;
        }

        var column = columnOverride ?? ColumnFor(attribute.Bag);
        var keyParam = $"attrKey{suffix}";
        parameters.AddParameter(keyParam, attribute.Key);
        var containsSql = $"mapContains({column}, {{{keyParam}:String}})";

        switch (attribute.Operator)
        {
            case SpanAttributeFilterOperator.Exists:
                return containsSql;
            case SpanAttributeFilterOperator.Absent:
                return $"NOT {containsSql}";
            case SpanAttributeFilterOperator.NotEquals:
            {
                var valueParam = $"attrValue{suffix}";
                parameters.AddParameter(valueParam, attribute.Value);
                return $"NOT ({containsSql} AND {column}[{{{keyParam}:String}}] = {{{valueParam}:String}})";
            }
            case SpanAttributeFilterOperator.Regex:
            {
                var valueParam = $"attrValue{suffix}";
                parameters.AddParameter(valueParam, attribute.Value);
                return $"({containsSql} AND match({column}[{{{keyParam}:String}}], {{{valueParam}:String}}))";
            }
            case SpanAttributeFilterOperator.NotRegex:
            {
                var valueParam = $"attrValue{suffix}";
                parameters.AddParameter(valueParam, attribute.Value);
                return $"NOT ({containsSql} AND match({column}[{{{keyParam}:String}}], {{{valueParam}:String}}))";
            }
            case SpanAttributeFilterOperator.In:
            {
                var valuesParam = $"attrValues{suffix}";
                parameters.AddParameter(valuesParam, (attribute.Values ?? []).ToArray());
                return $"({containsSql} AND {column}[{{{keyParam}:String}}] IN {{{valuesParam}:Array(String)}})";
            }
            case SpanAttributeFilterOperator.NotIn:
            {
                var valuesParam = $"attrValues{suffix}";
                parameters.AddParameter(valuesParam, (attribute.Values ?? []).ToArray());
                return $"NOT ({containsSql} AND {column}[{{{keyParam}:String}}] IN {{{valuesParam}:Array(String)}})";
            }
            case SpanAttributeFilterOperator.GreaterThan or SpanAttributeFilterOperator.GreaterThanOrEqual or SpanAttributeFilterOperator.LessThan or SpanAttributeFilterOperator.LessThanOrEqual:
                return NumericAttributeComparison.Clause($"{column}[{{{keyParam}:String}}]", NumericSymbol(attribute.Operator), attribute.Value, $"attrValue{suffix}", parameters);
            default:
            {
                var valueParam = $"attrValue{suffix}";
                parameters.AddParameter(valueParam, attribute.Value);
                return $"{column}[{{{keyParam}:String}}] = {{{valueParam}:String}}";
            }
        }
    }

    private static string NumericSymbol(SpanAttributeFilterOperator op) => op switch
    {
        SpanAttributeFilterOperator.GreaterThan => ">",
        SpanAttributeFilterOperator.GreaterThanOrEqual => ">=",
        SpanAttributeFilterOperator.LessThan => "<",
        _ => "<=",
    };

    /// <summary>
    /// <see cref="AttributeClause"/> against a promoted column - same operator rules as
    /// <see cref="LogFilterSqlBuilder"/>'s <c>PromotedClause</c> (ADR-0062): the column holds
    /// <c>Map[key]</c>, <c>''</c> for a missing key, so only <c>Equals</c> always switches,
    /// <c>NotEquals</c>/<c>In</c>/<c>NotIn</c> switch when no compared value is <c>''</c>, and
    /// every presence-sensitive operator returns null to keep the guarded map form.
    /// </summary>
    private static string? PromotedClause(SpanAttributeFilter attribute, string suffix, ClickHouseParameterCollection parameters, string column)
    {
        switch (attribute.Operator)
        {
            case SpanAttributeFilterOperator.Equals:
            {
                var valueParam = $"attrValue{suffix}";
                parameters.AddParameter(valueParam, attribute.Value);
                return $"{column} = {{{valueParam}:String}}";
            }
            case SpanAttributeFilterOperator.NotEquals when !string.IsNullOrEmpty(attribute.Value):
            {
                var valueParam = $"attrValue{suffix}";
                parameters.AddParameter(valueParam, attribute.Value);
                return $"{column} != {{{valueParam}:String}}";
            }
            case SpanAttributeFilterOperator.In or SpanAttributeFilterOperator.NotIn
                when attribute.Values is { Count: > 0 } values && !values.Any(string.IsNullOrEmpty):
            {
                var valuesParam = $"attrValues{suffix}";
                parameters.AddParameter(valuesParam, values.ToArray());
                var op = attribute.Operator == SpanAttributeFilterOperator.In ? "IN" : "NOT IN";
                return $"{column} {op} {{{valuesParam}:Array(String)}}";
            }
            case SpanAttributeFilterOperator.GreaterThan or SpanAttributeFilterOperator.GreaterThanOrEqual or SpanAttributeFilterOperator.LessThan or SpanAttributeFilterOperator.LessThanOrEqual:
                return NumericAttributeComparison.Clause(column, NumericSymbol(attribute.Operator), attribute.Value, $"attrValue{suffix}", parameters);
            default:
                return null;
        }
    }

    /// <summary>
    /// A <see cref="SpanAttributeBag"/> as the promoted-column snapshot keys it: promotion
    /// shares <see cref="AttributeBag"/> across tables, with <see cref="AttributeBag.Log"/>
    /// meaning the table's own attribute map (<c>SpanAttributes</c> here).
    /// </summary>
    internal static AttributeBag PromotedBag(SpanAttributeBag bag) => bag switch
    {
        SpanAttributeBag.Resource => AttributeBag.Resource,
        SpanAttributeBag.Scope => AttributeBag.Scope,
        _ => AttributeBag.Log,
    };

    /// <summary>
    /// The <c>Map(LowCardinality(String), String)</c> column a bag reads/writes.
    /// <c>internal</c> (not <c>private</c>) so <see cref="SpanAttributeValuesQueryBuilder"/>
    /// can resolve the same column for its own attribute-value-autocomplete query rather
    /// than re-deriving this switch a second time.
    /// </summary>
    internal static string ColumnFor(SpanAttributeBag bag) => bag switch
    {
        SpanAttributeBag.Resource => "ResourceAttributes",
        SpanAttributeBag.Scope => "ScopeAttributes",
        _ => "SpanAttributes",
    };
}
