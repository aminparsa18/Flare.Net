using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Pure <see cref="ResourceAttributeFilter"/> list → parameterized <c>WHERE</c>-clause
/// fragments, shared by <see cref="ServiceOverviewQueryBuilder"/>,
/// <see cref="ServiceDependencyQueryBuilder"/>, and
/// <see cref="ServiceCallBreakdownQueryBuilder"/> - the one place this clause shape is
/// written, rather than each of the three builders (and, for
/// <see cref="ServiceDependencyQueryBuilder"/>'s self-joined edges query, each of its two
/// aliased sides) re-deriving it. Same "pure function, no ClickHouse dependency" style as
/// <see cref="LogFilterSqlBuilder"/>/<see cref="SpanFilterSqlBuilder"/>'s own
/// <c>AttributeClause</c> helpers, simplified to equality-only - see
/// <see cref="ResourceAttributeFilter"/>'s own remarks for why.
/// </summary>
public static class ResourceAttributeFilterSqlBuilder
{
    /// <summary>
    /// Appends one AND'd equality clause per filter to <paramref name="clauses"/>, binding
    /// each key/value pair into <paramref name="parameters"/>. A no-op when
    /// <paramref name="resourceAttributes"/> is null/empty.
    /// </summary>
    /// <param name="columnAlias">
    /// Table alias/prefix for the <c>ResourceAttributes</c> column reference - empty for an
    /// unqualified single-table query, <c>"parent."</c>/<c>"child."</c> for
    /// <see cref="ServiceDependencyQueryBuilder"/>'s self-joined edges query.
    /// </param>
    /// <param name="paramPrefix">
    /// Disambiguates this call's bound parameter names from any other call against the
    /// same <see cref="ClickHouseParameterCollection"/> (needed when a caller applies the
    /// same filter list to two aliased sides of one query, e.g. both
    /// <c>"parent"</c> and <c>"child"</c> against the edges query's one parameter
    /// collection) - empty is fine for a query that only ever calls this once.
    /// </param>
    public static void AppendClauses(
        List<string> clauses,
        ClickHouseParameterCollection parameters,
        IReadOnlyList<ResourceAttributeFilter>? resourceAttributes,
        string columnAlias,
        string paramPrefix)
    {
        if (resourceAttributes is not { Count: > 0 })
        {
            return;
        }

        for (var i = 0; i < resourceAttributes.Count; i++)
        {
            var attribute = resourceAttributes[i];
            var keyParam = $"{paramPrefix}ResAttrKey{i}";
            var valueParam = $"{paramPrefix}ResAttrValue{i}";
            parameters.AddParameter(keyParam, attribute.Key);
            parameters.AddParameter(valueParam, attribute.Value);
            clauses.Add($"{columnAlias}ResourceAttributes[{{{keyParam}:String}}] = {{{valueParam}:String}}");
        }
    }
}
