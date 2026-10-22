using ClickHouse.Driver;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IDbStatementQueryService
{
    Task<DbStatementResponse> GetStatementsAsync(DbStatementRequest request, CancellationToken cancellationToken);
}

/// <summary>The one component holding an <see cref="IClickHouseClient"/> for the per-statement database view - see <see cref="DbStatementQueryBuilder"/> for the SQL.</summary>
public sealed class DbStatementQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IDbStatementQueryService
{
    public async Task<DbStatementResponse> GetStatementsAsync(DbStatementRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = DbStatementQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var sortBy = DbStatementQueryBuilder.ResolveSort(request.SortBy);
        var end = HostInventoryQueryBuilder.ResolveWindowEnd(request.EndUnixMs, timeProvider.GetUtcNow());

        var built = DbStatementQueryBuilder.Build(request, windowMinutes, sortBy, end);
        var statements = new List<DbStatementRow>();
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
        while (reader.Read())
        {
            var calls = reader.GetFieldValue<ulong>(3);
            var totalMs = reader.GetDouble(5) / 1e6;
            statements.Add(new DbStatementRow
            {
                ServiceName = reader.GetString(0),
                DbSystem = reader.GetString(1),
                Statement = reader.GetString(2),
                CallCount = calls,
                ErrorCount = reader.GetFieldValue<ulong>(4),
                CallsPerMinute = calls / (double)windowMinutes,
                AvgDurationMs = calls == 0 ? 0 : totalMs / calls,
                P95DurationMs = reader.GetDouble(6) / 1e6,
                TotalDurationMs = totalMs,
                ExampleTraceId = reader.GetString(7),
            });
        }

        return new DbStatementResponse { WindowMinutes = windowMinutes, SortBy = sortBy, Statements = statements };
    }
}
