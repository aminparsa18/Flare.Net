using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT ... ORDER BY rand() LIMIT n</c> for <c>POST /api/logs/value-distribution</c>, ready to hand to <see cref="LogQueryService"/>.</summary>
public sealed record LogValueDistributionSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="LogValueDistributionRequest"/> → parameterized SQL builder: a random
/// sample of (timestamp, value) pairs for one <c>LogAttributes</c> key, over whatever
/// window/filter is in scope. Powers the Logs page's Value distribution chart - all
/// time/value bucketing, the density→color mapping, and the linear/log y-axis toggle
/// happen client-side against this one response (see <c>ValueDistributionChart.svelte</c>),
/// the same "server aggregates/samples, client renders" split
/// <see cref="LogAggregateQueryBuilder"/> already uses for the Event volume chart.
/// </summary>
/// <remarks>
/// <c>ORDER BY rand() LIMIT n</c>, not <c>ORDER BY Timestamp LIMIT n</c> - the latter would
/// bias the sample toward the start of the time window on a busy window (the first n events
/// chronologically, not a spread across it), silently misrepresenting the distribution the
/// chart is supposed to show. This costs a full scan of the already time/filter-bounded
/// rows, same cost class <see cref="LogAggregateQueryBuilder"/>'s own remarks already
/// accept for this codebase's scale (no materialized views - see
/// <c>db/clickhouse/README.md</c>'s "ad hoc, freshness-first queries" note), and is bounded
/// the same way every other query here is, via <see cref="LogQueryService"/>'s
/// <c>SafetyOptions</c>.
/// </remarks>
public static class LogValueDistributionQueryBuilder
{
    public static LogValueDistributionSql Build(LogValueDistributionRequest request, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(request.AttributeKey))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.AttributeKey, "AttributeKey must be non-empty.");
        }

        if (request.SampleSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.SampleSize, "SampleSize must be positive.");
        }

        var filterSql = LogFilterSqlBuilder.Build(request.Filter ?? new LogFilter(), now);
        filterSql.Parameters.AddParameter("attributeKey", request.AttributeKey);
        filterSql.Parameters.AddParameter("sampleSize", request.SampleSize);

        // Value's WHERE check is in the outer query, against the subquery's projected
        // column - not `AS Value ... WHERE Value IS NOT NULL` in one SELECT level, which
        // isn't standard SQL (WHERE runs before the SELECT list's aliases exist) even
        // though ClickHouse tolerates it in some cases. Same subquery shape
        // LogAttributeKeysQueryBuilder already uses for the same reason.
        var sql = "SELECT Timestamp, Value\n" +
            "FROM (\n" +
            "    SELECT Timestamp, toFloat64OrNull(LogAttributes[{attributeKey:String}]) AS Value\n" +
            "    FROM logs\n" +
            $"    WHERE {filterSql.WhereSql}\n" +
            ")\n" +
            "WHERE Value IS NOT NULL\n" +
            "ORDER BY rand()\n" +
            "LIMIT {sampleSize:UInt32}";

        return new LogValueDistributionSql(sql, filterSql.Parameters);
    }
}
