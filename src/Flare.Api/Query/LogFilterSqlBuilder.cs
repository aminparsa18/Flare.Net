using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>A parameterized <c>WHERE</c> fragment (no leading <c>WHERE</c> keyword) plus its bound parameters.</summary>
public sealed record LogFilterSql(string WhereSql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="LogFilter"/> → parameterized <c>WHERE</c>-clause translation, shared
/// by <see cref="LogSearchQueryBuilder"/> and <see cref="LogAggregateQueryBuilder"/>.
/// Deliberately has no ClickHouse connection dependency - same "pure function,
/// unit-testable on its own" style as <c>Flare.Ingest</c>'s <c>ClickHouseRowMapper</c>.
/// </summary>
/// <remarks>
/// Every value that can carry request-supplied text (service names, severities,
/// attribute keys/values, trace id, search term, time range) is bound as a ClickHouse
/// query parameter - <c>ClickHouse.Driver</c>'s native <c>{name:Type}</c> placeholder
/// syntax, confirmed against the package's own release notes and a live parameter-binding
/// check, not assumed (see <c>Flare.Api</c>'s README). The only thing ever
/// string-interpolated directly into the SQL text is the attribute bag's *column name*
/// in <see cref="ColumnFor"/>, and that only ever comes from the closed
/// <see cref="AttributeBag"/> enum - never from request text.
/// </remarks>
public static class LogFilterSqlBuilder
{
    /// <summary>
    /// Default lookback applied when <see cref="LogFilter.From"/> is omitted. Keeps an
    /// otherwise-unfiltered request from scanning the whole table - per the
    /// `clickhouse-best-practices` skill's <c>agent-query-safety</c> rule ("never query
    /// without bounding the scan"), reinforced server-side by
    /// <see cref="LogQueryService"/>'s <c>max_rows_to_read</c>/<c>max_execution_time</c>
    /// settings on every query.
    /// </summary>
    public static readonly TimeSpan DefaultLookback = TimeSpan.FromHours(1);

    /// <summary>
    /// Builds a <c>%text%</c> substring pattern for <c>ILIKE</c> with the user's own text
    /// escaped, so a literal <c>%</c>, <c>_</c> or <c>\</c> (e.g. <c>user_id</c>,
    /// <c>100%</c>, <c>C:\temp</c>) matches itself rather than acting as a LIKE
    /// wildcard/escape character.
    /// </summary>
    public static string ContainsPattern(string text) =>
        $"%{text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_")}%";

    public static LogFilterSql Build(LogFilter filter, DateTimeOffset now)
    {
        var parameters = new ClickHouseParameterCollection();
        var clauses = new List<string>();

        var from = filter.From ?? now - DefaultLookback;
        var to = filter.To ?? now;
        parameters.AddParameter("from", from.UtcDateTime);
        parameters.AddParameter("to", to.UtcDateTime);
        clauses.Add("Timestamp >= {from:DateTime64(9)}");
        clauses.Add("Timestamp < {to:DateTime64(9)}");

        if (filter.Services is { Count: > 0 } services)
        {
            parameters.AddParameter("services", services.ToArray());
            clauses.Add("ServiceName IN {services:Array(String)}");
        }

        if (filter.SeverityNumbers is { Count: > 0 } severities)
        {
            parameters.AddParameter("severities", severities.ToArray());
            clauses.Add("SeverityNumber IN {severities:Array(UInt8)}");
        }

        if (filter.ScopeNames is { Count: > 0 } scopeNames)
        {
            clauses.Add(ScopeNamesClause(scopeNames, parameters));
        }

        if (!string.IsNullOrEmpty(filter.TraceId))
        {
            parameters.AddParameter("traceId", filter.TraceId);
            clauses.Add("TraceId = {traceId:String}");
        }

        if (!string.IsNullOrEmpty(filter.SpanId))
        {
            parameters.AddParameter("spanId", filter.SpanId);
            clauses.Add("SpanId = {spanId:String}");
        }

        if (!string.IsNullOrEmpty(filter.PatternId))
        {
            parameters.AddParameter("patternId", filter.PatternId);
            clauses.Add("PatternId = {patternId:String}");
        }

        if (!string.IsNullOrEmpty(filter.Search))
        {
            // Pattern is fully formed client-side and bound as one parameter value,
            // rather than built server-side via concat('%', {search:String}, '%') -
            // equivalent ILIKE semantics, one fewer moving part.
            parameters.AddParameter("search", ContainsPattern(filter.Search));
            clauses.Add("Body ILIKE {search:String}");
        }

        if (filter.Attributes is { Count: > 0 } attributes)
        {
            for (var i = 0; i < attributes.Count; i++)
            {
                clauses.Add(AttributeClause(attributes[i], i, parameters));
            }
        }

        if (filter.BodyJsonFilters is { Count: > 0 } bodyJsonFilters)
        {
            for (var i = 0; i < bodyJsonFilters.Count; i++)
            {
                clauses.Add(BodyJsonClause(bodyJsonFilters[i], i, parameters));
            }
        }

        return new LogFilterSql(string.Join(" AND ", clauses), parameters);
    }

    /// <summary>
    /// <see cref="LogFilter.ScopeNames"/>' clause: exact entries collapse into one
    /// <c>ScopeName IN</c>, each <c>*</c>-suffixed entry becomes its own
    /// <c>startsWith(ScopeName, prefix)</c>, all ORed in one parenthesized group. A bare
    /// <c>*</c> (empty prefix) matches every row, same as omitting the filter.
    /// </summary>
    private static string ScopeNamesClause(IReadOnlyList<string> scopeNames, ClickHouseParameterCollection parameters)
    {
        var (exact, prefixes) = SplitScopeNames(scopeNames);
        var alternatives = new List<string>();
        if (exact.Count > 0)
        {
            parameters.AddParameter("scopeNames", exact.ToArray());
            alternatives.Add("ScopeName IN {scopeNames:Array(String)}");
        }

        for (var i = 0; i < prefixes.Count; i++)
        {
            var prefixParam = $"scopePrefix{i}";
            parameters.AddParameter(prefixParam, prefixes[i]);
            alternatives.Add($"startsWith(ScopeName, {{{prefixParam}:String}})");
        }

        return $"({string.Join(" OR ", alternatives)})";
    }

    /// <summary>
    /// Splits <see cref="LogFilter.ScopeNames"/> into exact names and prefixes (the
    /// <c>*</c>-suffixed entries, with the <c>*</c> stripped). <c>internal</c> so
    /// <see cref="LogFilterMatcher"/> applies the exact same parsing in memory.
    /// </summary>
    internal static (List<string> Exact, List<string> Prefixes) SplitScopeNames(IReadOnlyList<string> scopeNames)
    {
        var exact = new List<string>();
        var prefixes = new List<string>();
        foreach (var name in scopeNames)
        {
            if (name.EndsWith('*'))
            {
                prefixes.Add(name[..^1]);
            }
            else
            {
                exact.Add(name);
            }
        }

        return (exact, prefixes);
    }

    /// <summary>
    /// One <see cref="AttributeFilter"/>'s clause. <see cref="AttributeFilterOperator.Exists"/>/
    /// <see cref="AttributeFilterOperator.Absent"/> compile to <c>mapContains</c> alone (no
    /// value parameter bound - <see cref="AttributeFilter.Value"/> is unused);
    /// <see cref="AttributeFilterOperator.NotEquals"/> explicitly guards with
    /// <c>mapContains</c> too, rather than relying on the map's empty-string default for a
    /// missing key, so it matches <see cref="LogFilterMatcher"/>'s in-memory semantics
    /// exactly: a record missing the key counts as "not equal", same as one that has it
    /// with a different value. <see cref="AttributeFilterOperator.Regex"/>/
    /// <see cref="AttributeFilterOperator.NotRegex"/> compile to ClickHouse's <c>match()</c>
    /// (RE2 syntax) instead of <c>=</c>, guarded by <c>mapContains</c> the same way - a
    /// missing key never reaches <c>match()</c> against the map's empty-string default, so
    /// <c>Regex</c> requires presence and <c>NotRegex</c> treats absence as "no match" like
    /// <c>NotEquals</c> does. <see cref="AttributeFilterOperator.In"/>/
    /// <see cref="AttributeFilterOperator.NotIn"/> compile to <c>IN</c> against
    /// <see cref="AttributeFilter.Values"/> (bound as an <c>Array(String)</c> parameter,
    /// empty when <c>Values</c> is null) instead of <c>=</c> against <c>Value</c>, guarded by
    /// <c>mapContains</c> the same way as every other multi-branch operator here.
    /// </summary>
    private static string AttributeClause(AttributeFilter attribute, int index, ClickHouseParameterCollection parameters)
    {
        var column = ColumnFor(attribute.Bag);
        var keyParam = $"attrKey{index}";
        parameters.AddParameter(keyParam, attribute.Key);
        var containsSql = $"mapContains({column}, {{{keyParam}:String}})";

        switch (attribute.Operator)
        {
            case AttributeFilterOperator.Exists:
                return containsSql;
            case AttributeFilterOperator.Absent:
                return $"NOT {containsSql}";
            case AttributeFilterOperator.NotEquals:
            {
                var valueParam = $"attrValue{index}";
                parameters.AddParameter(valueParam, attribute.Value);
                return $"NOT ({containsSql} AND {column}[{{{keyParam}:String}}] = {{{valueParam}:String}})";
            }
            case AttributeFilterOperator.Regex:
            {
                var valueParam = $"attrValue{index}";
                parameters.AddParameter(valueParam, attribute.Value);
                return $"({containsSql} AND match({column}[{{{keyParam}:String}}], {{{valueParam}:String}}))";
            }
            case AttributeFilterOperator.NotRegex:
            {
                var valueParam = $"attrValue{index}";
                parameters.AddParameter(valueParam, attribute.Value);
                return $"NOT ({containsSql} AND match({column}[{{{keyParam}:String}}], {{{valueParam}:String}}))";
            }
            case AttributeFilterOperator.In:
            {
                var valuesParam = $"attrValues{index}";
                parameters.AddParameter(valuesParam, (attribute.Values ?? []).ToArray());
                return $"({containsSql} AND {column}[{{{keyParam}:String}}] IN {{{valuesParam}:Array(String)}})";
            }
            case AttributeFilterOperator.NotIn:
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
    /// One <see cref="BodyJsonFilter"/>'s clause, against <c>Body</c> rather than an
    /// attribute-bag column. <see cref="BodyJsonFilter.Path"/> is split on <c>.</c> into
    /// separate <c>String</c> parameters, one per <c>JSONHas</c>/<c>JSONExtractString</c>
    /// key argument (ClickHouse's variadic-key form, not a single JSONPath-string argument -
    /// confirmed live against a real ClickHouse instance, including that malformed/non-JSON
    /// <c>Body</c> makes both functions return their zero value rather than throw, so this
    /// clause is always safe to evaluate regardless of whether a given row's <c>Body</c> is
    /// JSON at all). <see cref="BodyJsonFilterOperator.Equals"/> is intentionally left
    /// unguarded by <c>JSONHas</c> - same as <see cref="AttributeClause"/>'s own default
    /// (<see cref="AttributeFilterOperator.Equals"/>) case - since <c>JSONExtractString</c>
    /// already returns <c>''</c> for an absent path, so an unguarded <c>=</c> only matches
    /// absence when <see cref="BodyJsonFilter.Value"/> is itself empty, exactly like a
    /// missing map key.
    /// </summary>
    private static string BodyJsonClause(BodyJsonFilter filter, int index, ClickHouseParameterCollection parameters)
    {
        var segments = filter.Path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var segmentArgs = new string[segments.Length];
        for (var s = 0; s < segments.Length; s++)
        {
            var segParam = $"jsonPath{index}_{s}";
            parameters.AddParameter(segParam, segments[s]);
            segmentArgs[s] = $"{{{segParam}:String}}";
        }

        var pathArgsSql = string.Join(", ", segmentArgs);
        var hasSql = $"JSONHas(Body, {pathArgsSql})";
        var extractSql = $"JSONExtractString(Body, {pathArgsSql})";

        switch (filter.Operator)
        {
            case BodyJsonFilterOperator.Exists:
                return hasSql;
            case BodyJsonFilterOperator.Absent:
                return $"NOT {hasSql}";
            case BodyJsonFilterOperator.NotEquals:
            {
                var valueParam = $"jsonValue{index}";
                parameters.AddParameter(valueParam, filter.Value);
                return $"NOT ({hasSql} AND {extractSql} = {{{valueParam}:String}})";
            }
            case BodyJsonFilterOperator.Regex:
            {
                var valueParam = $"jsonValue{index}";
                parameters.AddParameter(valueParam, filter.Value);
                return $"({hasSql} AND match({extractSql}, {{{valueParam}:String}}))";
            }
            case BodyJsonFilterOperator.NotRegex:
            {
                var valueParam = $"jsonValue{index}";
                parameters.AddParameter(valueParam, filter.Value);
                return $"NOT ({hasSql} AND match({extractSql}, {{{valueParam}:String}}))";
            }
            case BodyJsonFilterOperator.In:
            {
                var valuesParam = $"jsonValues{index}";
                parameters.AddParameter(valuesParam, (filter.Values ?? []).ToArray());
                return $"({hasSql} AND {extractSql} IN {{{valuesParam}:Array(String)}})";
            }
            case BodyJsonFilterOperator.NotIn:
            {
                var valuesParam = $"jsonValues{index}";
                parameters.AddParameter(valuesParam, (filter.Values ?? []).ToArray());
                return $"NOT ({hasSql} AND {extractSql} IN {{{valuesParam}:Array(String)}})";
            }
            // JSONExtract(..., 'Array(String)') stringifies each element the same way
            // JSONExtractString does a scalar (and returns [] for a missing path, a
            // non-array value, or non-JSON Body), so no JSONHas guard is needed here.
            case BodyJsonFilterOperator.Has:
            {
                var valueParam = $"jsonValue{index}";
                parameters.AddParameter(valueParam, filter.Value);
                return $"has(JSONExtract(Body, {pathArgsSql}, 'Array(String)'), {{{valueParam}:String}})";
            }
            case BodyJsonFilterOperator.NotHas:
            {
                var valueParam = $"jsonValue{index}";
                parameters.AddParameter(valueParam, filter.Value);
                return $"NOT has(JSONExtract(Body, {pathArgsSql}, 'Array(String)'), {{{valueParam}:String}})";
            }
            default:
            {
                var valueParam = $"jsonValue{index}";
                parameters.AddParameter(valueParam, filter.Value);
                return $"{extractSql} = {{{valueParam}:String}}";
            }
        }
    }

    /// <summary>
    /// The <c>Map(LowCardinality(String), String)</c> column a bag reads/writes.
    /// <c>internal</c> (not <c>private</c>) so <see cref="LogAttributeValuesQueryBuilder"/>
    /// can resolve the same column for its own attribute-value-autocomplete query rather
    /// than re-deriving this switch a second time.
    /// </summary>
    internal static string ColumnFor(AttributeBag bag) => bag switch
    {
        AttributeBag.Resource => "ResourceAttributes",
        AttributeBag.Scope => "ScopeAttributes",
        _ => "LogAttributes",
    };
}
