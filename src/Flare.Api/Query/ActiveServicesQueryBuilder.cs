using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;

namespace Flare.Api.Query;

/// <summary>Fully-built <c>SELECT ... GROUP BY</c> for the Resources page's producer-services overlay, ready to hand to <see cref="LogQueryService"/>.</summary>
public sealed record ActiveServicesSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure window → parameterized SQL builder for "which services have sent a log event
/// recently" - the data behind <see cref="DockerResources.DockerContainerPoller"/>'s
/// producer-node overlay (see that type's remarks). Split out from
/// <see cref="LogQueryService"/> for the same reason <see cref="LogAggregateQueryBuilder"/>
/// is: pure, unit-testable SQL construction with no ClickHouse dependency.
/// </summary>
public static class ActiveServicesQueryBuilder
{
    public static ActiveServicesSql Build(TimeSpan window, DateTimeOffset now)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("since", (now - window).UtcDateTime);

        // ServiceName is the leading ORDER BY column on `logs` (see db/clickhouse/0001_logs.sql)
        // so this GROUP BY is a plan already favorable to that layout; Timestamp isn't the
        // leading key, so the WHERE clause still means a bounded-by-partition scan rather
        // than a tight primary-key range - acceptable at Flare's stated scale, same
        // tradeoff LogAggregateQueryBuilder's own remarks already document for this table.
        const string sql = "SELECT ServiceName, max(Timestamp) AS LastSeenAt\n" +
            "FROM logs\n" +
            "WHERE Timestamp >= {since:DateTime64(9)}\n" +
            "GROUP BY ServiceName\n" +
            "ORDER BY LastSeenAt DESC";

        return new ActiveServicesSql(sql, parameters);
    }
}
