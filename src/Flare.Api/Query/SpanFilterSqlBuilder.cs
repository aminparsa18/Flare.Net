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
                clauses.Add(AttributeClause(attributes[i], i, parameters, promoted));
            }
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
    /// against <c>Value</c>, guarded the same way too.
    /// </summary>
    private static string AttributeClause(SpanAttributeFilter attribute, int index, ClickHouseParameterCollection parameters, PromotedAttributeColumns? promoted)
    {
        if (promoted is not null && promoted.TryGetColumn(PromotedBag(attribute.Bag), attribute.Key, out var promotedColumn)
            && PromotedClause(attribute, index, parameters, promotedColumn) is { } promotedSql)
        {
            return promotedSql;
        }

        var column = ColumnFor(attribute.Bag);
        var keyParam = $"attrKey{index}";
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
                var valueParam = $"attrValue{index}";
                parameters.AddParameter(valueParam, attribute.Value);
                return $"NOT ({containsSql} AND {column}[{{{keyParam}:String}}] = {{{valueParam}:String}})";
            }
            case SpanAttributeFilterOperator.Regex:
            {
                var valueParam = $"attrValue{index}";
                parameters.AddParameter(valueParam, attribute.Value);
                return $"({containsSql} AND match({column}[{{{keyParam}:String}}], {{{valueParam}:String}}))";
            }
            case SpanAttributeFilterOperator.NotRegex:
            {
                var valueParam = $"attrValue{index}";
                parameters.AddParameter(valueParam, attribute.Value);
                return $"NOT ({containsSql} AND match({column}[{{{keyParam}:String}}], {{{valueParam}:String}}))";
            }
            case SpanAttributeFilterOperator.In:
            {
                var valuesParam = $"attrValues{index}";
                parameters.AddParameter(valuesParam, (attribute.Values ?? []).ToArray());
                return $"({containsSql} AND {column}[{{{keyParam}:String}}] IN {{{valuesParam}:Array(String)}})";
            }
            case SpanAttributeFilterOperator.NotIn:
            {
                var valuesParam = $"attrValues{index}";
                parameters.AddParameter(valuesParam, (attribute.Values ?? []).ToArray());
                return $"NOT ({containsSql} AND {column}[{{{keyParam}:String}}] IN {{{valuesParam}:Array(String)}})";
            }
            default:
            {
                var valueParam = $"attrValue{index}";
                parameters.AddParameter(valueParam, attribute.Value);
                return $"{column}[{{{keyParam}:String}}] = {{{valueParam}:String}}";
            }
        }
    }

    /// <summary>
    /// <see cref="AttributeClause"/> against a promoted column - same operator rules as
    /// <see cref="LogFilterSqlBuilder"/>'s <c>PromotedClause</c> (ADR-0062): the column holds
    /// <c>Map[key]</c>, <c>''</c> for a missing key, so only <c>Equals</c> always switches,
    /// <c>NotEquals</c>/<c>In</c>/<c>NotIn</c> switch when no compared value is <c>''</c>, and
    /// every presence-sensitive operator returns null to keep the guarded map form.
    /// </summary>
    private static string? PromotedClause(SpanAttributeFilter attribute, int index, ClickHouseParameterCollection parameters, string column)
    {
        switch (attribute.Operator)
        {
            case SpanAttributeFilterOperator.Equals:
            {
                var valueParam = $"attrValue{index}";
                parameters.AddParameter(valueParam, attribute.Value);
                return $"{column} = {{{valueParam}:String}}";
            }
            case SpanAttributeFilterOperator.NotEquals when !string.IsNullOrEmpty(attribute.Value):
            {
                var valueParam = $"attrValue{index}";
                parameters.AddParameter(valueParam, attribute.Value);
                return $"{column} != {{{valueParam}:String}}";
            }
            case SpanAttributeFilterOperator.In or SpanAttributeFilterOperator.NotIn
                when attribute.Values is { Count: > 0 } values && !values.Any(string.IsNullOrEmpty):
            {
                var valuesParam = $"attrValues{index}";
                parameters.AddParameter(valuesParam, values.ToArray());
                var op = attribute.Operator == SpanAttributeFilterOperator.In ? "IN" : "NOT IN";
                return $"{column} {op} {{{valuesParam}:Array(String)}}";
            }
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
