using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT ... GROUP BY</c> for <c>POST /api/logs/attribute-values</c>, ready to hand to <see cref="LogQueryService"/>.</summary>
public sealed record LogAttributeValuesSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="LogAttributeValuesRequest"/> → parameterized SQL builder: every distinct
/// value one <c>LogAttributes</c>/<c>ResourceAttributes</c>/<c>ScopeAttributes</c> key holds
/// across in-scope events, with how many carry it - the Attribute filters builder's value
/// autocomplete (<c>AttributeFiltersRow.svelte</c>'s value input). Deliberately no subquery
/// (unlike <see cref="LogValueDistributionQueryBuilder"/>'s <c>toFloat64OrNull</c> null
/// check) - <c>mapContains</c> in the outer <c>WHERE</c> already guarantees the map access
/// in <c>SELECT</c>/<c>GROUP BY</c> is well-defined, so there's no alias to smuggle past a
/// same-level <c>WHERE</c> the way that builder's remarks describe.
/// </summary>
public static class LogAttributeValuesQueryBuilder
{
    /// <summary>
    /// Most-recent JSON-looking events <see cref="LogValuesField.BodyJsonPath"/>/
    /// <see cref="LogValuesField.BodyJsonValue"/> inspect. <c>Body</c> has no key index, so
    /// these suggestions are a bounded sample (newest first) rather than an exhaustive scan.
    /// </summary>
    internal const int BodyJsonSampleSize = 2000;

    public static LogAttributeValuesSql Build(LogAttributeValuesRequest request, DateTimeOffset now, PromotedAttributeColumns? promoted = null)
    {
        if (request.Field == LogValuesField.Attribute && string.IsNullOrEmpty(request.Key))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Key, "Key must be non-empty.");
        }

        if (request.Limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Limit, "Limit must be positive.");
        }

        var filterSql = LogFilterSqlBuilder.Build(request.Filter ?? new LogFilter(), now, promoted);
        filterSql.Parameters.AddParameter("valuesLimit", request.Limit);

        if (request.Field is LogValuesField.BodyJsonPath or LogValuesField.BodyJsonValue)
        {
            return BuildBodyJson(request, filterSql);
        }

        var whereClauses = new List<string> { filterSql.WhereSql };
        string valueSql;
        switch (request.Field)
        {
            case LogValuesField.Service:
                valueSql = "ServiceName";
                break;
            case LogValuesField.Severity:
                valueSql = "toString(SeverityNumber)";
                break;
            default:
                var column = LogFilterSqlBuilder.ColumnFor(request.Bag);
                filterSql.Parameters.AddParameter("valuesKey", request.Key);
                valueSql = $"{column}[{{valuesKey:String}}]";
                whereClauses.Add($"mapContains({column}, {{valuesKey:String}})");
                break;
        }

        if (!string.IsNullOrEmpty(request.Prefix))
        {
            // Same substring/case-insensitive ILIKE convention LogFilterSqlBuilder.Build
            // uses for LogFilter.Search - the caller's already-typed text narrows candidates
            // rather than requiring it as an exact/anchored match.
            filterSql.Parameters.AddParameter("valuesPrefix", LogFilterSqlBuilder.ContainsPattern(request.Prefix));
            whereClauses.Add($"{valueSql} ILIKE {{valuesPrefix:String}}");
        }

        var sql = $"SELECT {valueSql} AS Value, count() AS Cnt\n" +
            "FROM logs\n" +
            $"WHERE {string.Join(" AND ", whereClauses)}\n" +
            "GROUP BY Value\n" +
            "ORDER BY Cnt DESC\n" +
            "LIMIT {valuesLimit:UInt32}";

        return new LogAttributeValuesSql(sql, filterSql.Parameters);
    }

    /// <summary>
    /// Samples the newest <see cref="BodyJsonSampleSize"/> in-scope events whose <c>Body</c>
    /// looks like a JSON object, then enumerates either the child keys under the path
    /// (<c>JSONExtractKeys</c>) or the scalar values at it (<c>JSONExtractString</c>). The
    /// sample is a subquery so <c>LIMIT</c> applies before the <c>arrayJoin</c>/<c>GROUP BY</c>.
    /// Path segments are bound as separate <c>String</c> parameters, same as
    /// <see cref="LogFilterSqlBuilder"/>'s body-JSON clause.
    /// </summary>
    private static LogAttributeValuesSql BuildBodyJson(LogAttributeValuesRequest request, LogFilterSql filterSql)
    {
        var segments = (request.Key ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries);
        var segmentArgs = new string[segments.Length];
        for (var s = 0; s < segments.Length; s++)
        {
            filterSql.Parameters.AddParameter($"valuesJsonPath{s}", segments[s]);
            segmentArgs[s] = $"{{valuesJsonPath{s}:String}}";
        }

        var pathArgsSql = string.Join(", ", segmentArgs);
        var pathPrefixSql = segments.Length > 0 ? ", " + pathArgsSql : "";

        string sampleSelect;
        var sampleWhere = $"{filterSql.WhereSql} AND Body LIKE '{{%'";
        if (request.Field == LogValuesField.BodyJsonPath)
        {
            sampleSelect = $"arrayJoin(JSONExtractKeys(Body{pathPrefixSql})) AS Value";
        }
        else
        {
            if (segments.Length == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), request.Key, "Key must be non-empty.");
            }

            sampleSelect = $"JSONExtractString(Body, {pathArgsSql}) AS Value";
            sampleWhere += $" AND JSONHas(Body, {pathArgsSql})";
        }

        var outerWhere = "Value != ''";
        if (!string.IsNullOrEmpty(request.Prefix))
        {
            filterSql.Parameters.AddParameter("valuesPrefix", LogFilterSqlBuilder.ContainsPattern(request.Prefix));
            outerWhere += " AND Value ILIKE {valuesPrefix:String}";
        }

        filterSql.Parameters.AddParameter("valuesSample", BodyJsonSampleSize);
        var sql = "SELECT Value, count() AS Cnt\n" +
            "FROM (\n" +
            $"    SELECT {sampleSelect}\n" +
            "    FROM (\n" +
            $"        SELECT Body FROM logs WHERE {sampleWhere}\n" +
            "        ORDER BY Timestamp DESC LIMIT {valuesSample:UInt32}\n" +
            "    )\n" +
            ")\n" +
            $"WHERE {outerWhere}\n" +
            "GROUP BY Value\n" +
            "ORDER BY Cnt DESC\n" +
            "LIMIT {valuesLimit:UInt32}";

        return new LogAttributeValuesSql(sql, filterSql.Parameters);
    }
}
